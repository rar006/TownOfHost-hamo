using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TownOfHost.Templates;

/// <summary>
/// Among Us標準の TextBoxTMP (プレイヤー名入力・部屋コード入力などに使われている、
/// 実機で動作実績のある入力欄コンポーネント) を薄くラップした、汎用のテキスト入力欄。
///
/// 重要: TextBoxTMP本体が内部で使う outputText の子テキストは特殊な座標系を持っており、
/// Modules/CustomSpawn/CustomSpawnEditor.cs の実績あるコードでは
/// text.transform.localPosition が (10.2f, 0.025f) という一見不自然な値になっている。
/// これは実機で動作確認済みの値のため、幅・高さに関わらずこの値をそのまま使い、
/// 独自の計算式を当てはめない。outputTextには一切手を加えず、
/// 「実際に画面へ見せる複数行表示」は完全に独立した別のTextMeshProオブジェクト(_displayText)
/// として用意し、そちらだけを通常の座標で配置する。
///
/// 折り返し・行送りについて: 過去、1行あたりの文字数や行送りを目視の推測値で固定していたが、
/// 実機のフォント・解像度によって値がズレ、テキストが枠からはみ出す不具合を繰り返した。
/// そのため、折り返し自体はTextMeshPro自身(enableWordWrapping=true)に計算させ、
/// 表示行数の見積もりにはRoleGuideButtonPatch.csで実績のある行送り係数
/// (フォントサイズ×0.235)を使う。
///
/// カーソルについて: 別オブジェクトの座標計算でカーソルを追従させる方式も、行送りの
/// 実測値がズレるたびに位置がずれる不具合を繰り返したため、
/// 「表示テキストの末尾に'|'を直接連結する」方式に変更している。
///
/// 制約について(過去の制約と、今回の対応): TextBoxTMP(Among Us標準コンポーネント)自体は
/// 「末尾への追記・末尾からの削除」のみに対応しており、文中の任意位置へカーソルを移動して
/// 挿入/削除する機能を持たない。そのため本クラスでは、TextBoxTMPを「クリックでフォーカスを
/// 得るための見た目上の受け皿」としてのみ使い、実際の文字入力・削除・カーソル移動は
/// すべてTextBoxCaretBlinker側でInput.inputStringと矢印キー等を直接監視して自前で行う
/// ことで、矢印キーでの移動や文中への挿入・削除(途中編集)を実現している。
/// </summary>
public class SimpleTextBox
{
    public TextBoxTMP TextBox { get; }
    // 実際に画面へ表示する全文。TextBoxTMP自身は改行文字を保持できないことに加え、
    // 末尾への追記・削除しか行えないため、真実の情報源はこちら側で完全に管理する。
    private string _fullText = "";
    public string Text => _fullText;
    /// <summary>入力内容を表示しているTMP(フォント・色・描画順を外から調整する用)</summary>
    public TMPro.TextMeshPro DisplayText => _displayText;

    // カーソル位置(_fullText中の文字インデックス、0 = 先頭)。
    // 矢印キーでの移動や、文中への挿入/削除に使う「途中編集」の要。
    private int _cursorIndex;
    private readonly int _characterLimit;

    private readonly TextMeshPro _placeholderText;
    private readonly TextMeshPro _displayText;
    private bool _caretVisible;
    private TextBoxCaretBlinker _caretBlinker;
    private readonly bool _multiline;
    public bool IsMultiline => _multiline;
    private readonly int _maxVisibleLines;
    private readonly int _charsPerLine;
    // 実測ベースの行送り係数。
    // 以前の値(0.235)は、実際にTextMeshProが描画する行送りの倍近くを
    // 見積もってしまっており、その結果、枠の中に本当はもっと入るのに
    // 8行程度で頭打ちになり、枠の下半分が丸ごと空くという不具合があった。
    // スクリーンショットの実測(枠の高さと実際に収まった行数の比率)をもとに
    // 0.235 → 0.135に修正する。
    private const float LineHeightRatio = 0.135f;
    private readonly float _fontSize;
    private float LineHeight => _fontSize * LineHeightRatio;

    // 手動スクロール用: 現在表示している「先頭行」のインデックス(0始まり)。
    private int _scrollLine;
    private string[] _wrappedLines = Array.Empty<string>();
    // _wrappedLines[i]の直後に実際の改行文字('\n')が1つあるかどうか。
    // (自動折り返しによる行分割の境界には、実際の文字は存在しないため、
    //  カーソル位置の行/列を正しく逆算するにはこの情報が必須になる)
    private bool[] _wrappedLineHasNewlineAfter = Array.Empty<bool>();

    public SimpleTextBox(
        Transform parent,
        string name,
        Vector3 localPosition,
        float width = 3f,
        float height = 0.25f,
        int characterLimit = 200,
        string placeholder = "",
        Action onFocusLost = null,
        Action onFocusGained = null,
        Action onEnter = null,
        bool multiline = false)
    {
        _multiline = multiline;
        _characterLimit = characterLimit;

        var collider = new GameObject(name).AddComponent<BoxCollider2D>();
        TextBox = collider.gameObject.AddComponent<TextBoxTMP>();
        var button = TextBox.gameObject.AddComponent<PassiveButton>();
        button.Colliders = new Collider2D[] { collider };

        // ===== TextBoxTMP本体が使う内部テキスト(outputText) =====
        // これは実機動作を検証済みのCustomSpawnEditor.CreateTextBoxと完全に同じ設定にする。
        // 座標・サイズなど一切を独自にいじらない(過去、ここを弄ったことが不具合の元になっていた)。
        var text = new GameObject("Text").AddComponent<TextMeshPro>();

        TextBox.AllowEmail = false;
        TextBox.AllowSymbols = true;
        TextBox.AllowPaste = true;
        TextBox.allowAllCharacters = true;
        TextBox.tempTxt = new();
        TextBox.outputText = text;
        TextBox.compoText = "";
        TextBox.text = "";
        TextBox.characterLimit = characterLimit;
        TextBox.OnChange = new();
        TextBox.OnEnter = new();
        TextBox.OnFocusLost = new();

        if (parent) TextBox.transform.SetParent(parent, false);
        TextBox.transform.localPosition = localPosition;

        button.OnMouseOut = new();
        button.OnMouseOver = new();
        button.OnClick = new();
        button.OnClick.AddListener((Action)(() =>
        {
            TextBox.GiveFocus();
            onFocusGained?.Invoke();
            SetCaretVisible(true);
            RefreshDisplay(scrollToEnd: false);
        }));

        collider.offset = new Vector2(width / 2f, 0);
        collider.size = new Vector2(width, height);

        text.fontSize = text.fontSizeMax = text.fontSizeMin = 2f;
        text.alignment = TextAlignmentOptions.Left;
        text.transform.SetParent(TextBox.transform, false);
        // 実績のある値をそのまま使う。width/heightに応じて独自計算しない。
        text.transform.localPosition = new Vector3(10.2f, 0.025f, 0f);

        TextBox.gameObject.layer = LayerMask.NameToLayer("UI");
        text.gameObject.layer = LayerMask.NameToLayer("UI");

        // outputText自体は非表示にし、見た目は_displayText側だけで担当する。
        // (画面外(x=10.2)に配置されているため実際には映らないが、
        //  environmentによってはenabled=falseだけでは描画が残ることがあるため、
        //  念のため透明化・サイズ最小化も併用しておく)
        text.enabled = false;
        text.color = new Color(1f, 1f, 1f, 0f);
        text.fontSize = 0.01f;

        // ===== 画面に実際に見せる表示用テキスト =====
        // TextBoxTMPの内部座標系とは無関係に、通常のワールド座標で入力欄の左上を基準に置く。
        var displayObj = new GameObject("DisplayText").AddComponent<TextMeshPro>();
        _displayText = displayObj;
        _fontSize = 1.9f;
        _displayText.transform.SetParent(TextBox.transform, false);
        _displayText.fontSize = _displayText.fontSizeMax = _displayText.fontSizeMin = _fontSize;
        _displayText.color = Color.white;
        _displayText.enabled = true;
        _displayText.richText = false;
        _displayText.alignment = TextAlignmentOptions.TopLeft;
        // 半角(欧文フォールバックフォント)と全角(日本語フォント)とで行の高さが
        // 微妙に異なって見える件について、TextMeshPro側の自動行間調整を無効化し、
        // 常に固定の行間になるようにする。
        _displayText.lineSpacing = 0f;
        _displayText.gameObject.layer = LayerMask.NameToLayer("UI");
        // RectTransformのpivot/anchorはデフォルトで中央(0.5, 0.5)になっており、
        // これを直さないままlocalPositionを設定すると、指定した座標を「矩形の中心」として
        // 上下左右に広がってしまい、テキストが枠の外(特に左側)へはみ出す不具合の原因になる。
        // 左上を基準にしたいので、pivotとanchorを明示的に(0, 1)に揃える。
        _displayText.rectTransform.pivot = new Vector2(0f, 1f);
        _displayText.rectTransform.anchorMin = new Vector2(0f, 1f);
        _displayText.rectTransform.anchorMax = new Vector2(0f, 1f);
        // 入力欄の左上を基準に配置する。TextBox.transform自体は「枠の中心」に置かれる
        // 前提で呼び出されるため、中心から上端までの距離(height/2)だけ上に上げる。
        // 要望により、右端ギリギリまでテキストを使えるよう、右側の余白を最小限にする。
        var textAreaWidth = Mathf.Max(width - 0.16f, 0.5f);
        // 開始位置は、入力欄のクリック判定(コライダー)の範囲内に収まるよう、
        // 上端(height/2f)を超えない範囲でできるだけ上に寄せる。
        // (これを超えて上に飛び出すと、見た目のテキストとクリックできる場所がズレて
        //  「枠の下の方しかクリックできない」不具合になる)
        // 要望により、テキストの開始位置をさらに上端ギリギリまで寄せる。
        _displayText.transform.localPosition = new Vector3(0.08f, height / 2f - 0.04f, -0.02f);
        // TextMeshPro自身の単語折り返し(enableWordWrapping)は、スペースを含まない長い
        // 連続文字列(特に半角英数字)を「1つの単語」とみなし、単語の途中では折り返さない
        // ため、半角文字が続くと枠を突き破ってしまう。そのため、折り返しは自前の
        // WrapLines(固定文字数での機械的な折り返し)で行い、TextMeshPro側の
        // 折り返し機能そのものは無効化しておく。
        _displayText.enableWordWrapping = false;
        _displayText.overflowMode = TextOverflowModes.Overflow;
        _displayText.rectTransform.sizeDelta = new Vector2(textAreaWidth, Mathf.Max(height, 0.3f));

        // 1行あたりの最大文字数(半角換算)。半角文字を基準に、安全側に倒して少なめに見積もる。
        // 全角文字はWrapLines内で2文字分として計算されるため、全角のみの行なら
        // 実際にはこの半分程度の文字数で折り返される。
        // 要望により、実際に入る文字数ぎりぎりまで詰められるよう、安全マージンを減らして
        // 1行に入る文字数を増やす(係数を0.11から0.095に縮小)。
        // → さらに要望により、そこからもう2倍に増やす(0.095 → 0.0475)。
        // → 右側にはみ出しすぎたため、要望により右から7文字ぶん(半角換算)減らす。
        //   (入力欄自体の大きさ・横幅は変えない)
        _charsPerLine = Mathf.Max(4, Mathf.FloorToInt(textAreaWidth / (_fontSize * 0.0475f)) - 7);
        // 枠の高さに収まる行数を、実測ベースの行送りから算出する。
        // 開始位置を上端ギリギリ(-0.15f)にした分、安全マージンは最小限にとどめる。
        _maxVisibleLines = Mathf.Max(1, Mathf.FloorToInt((height - 0.05f) / LineHeight));

        // 注意: TextBoxTMP自身の入力検知(OnChange)には依存しない。
        // TextBoxTMPは「末尾への追記・削除のみ」対応という制約があり、文中編集を
        // 実現できないため、実際の文字入力・削除・カーソル移動はすべて
        // TextBoxCaretBlinker.Update内でInput.inputStringと矢印キー等を毎フレーム
        // 直接監視して行う。TextBoxTMPは「クリックでフォーカスを得る」ための
        // 見た目上の受け皿としてのみ使う。
        // TextBoxTMP自身が(バニラ側の処理で)文字を溜め込んでしまうと、_fullTextとの
        // 二重管理で不整合が起きるため、何か変化があるたびに強制的に空へ戻しておく。
        TextBox.OnChange.AddListener((Action)(() =>
        {
            if (!string.IsNullOrEmpty(TextBox.text))
            {
                TextBox.SetText("");
                if (TextBox.outputText != null) TextBox.outputText.text = "";
            }
        }));

        if (onEnter != null) TextBox.OnEnter.AddListener((Action)onEnter);
        if (onFocusLost != null) TextBox.OnFocusLost.AddListener((Action)onFocusLost);
        TextBox.OnFocusLost.AddListener((Action)(() =>
        {
            SetCaretVisible(false);
        }));

        if (multiline)
        {
            var scrollHandler = TextBox.gameObject.AddComponent<TextBoxScrollHandler>();
            scrollHandler.Owner = this;
        }

        if (!string.IsNullOrEmpty(placeholder))
        {
            _placeholderText = UnityEngine.Object.Instantiate(_displayText, TextBox.transform);
            _placeholderText.gameObject.name = "Placeholder";
            _placeholderText.text = placeholder;
            _placeholderText.color = new Color(1f, 1f, 1f, 0.35f);
            _placeholderText.transform.localPosition = _displayText.transform.localPosition + new Vector3(0f, 0f, -0.01f);
        }

        // ===== 入力位置が分かるよう、末尾に点滅カーソルを表示 =====
        // TextBoxTMPは常に末尾への追記のみ(途中へのカーソル移動は非対応)のため、
        // 「次に打った文字がどこに入るか」を示す。
        // 過去、カーソルを別オブジェクトの座標計算で追従させる方式は、行送りの実測値が
        // 実機とズレるたびに位置がずれる不具合を繰り返し起こしていたため、
        // 「表示テキストの末尾に'|'を直接連結する」方式に変更し、座標計算そのものを廃止する。
        _caretBlinker = TextBox.gameObject.AddComponent<TextBoxCaretBlinker>();
        _caretBlinker.Owner = this;

        RefreshDisplay(scrollToEnd: false);
    }

    /// <summary>点滅カーソルの表示/非表示切り替えに応じて、表示テキストを再描画する。</summary>
    internal void SetCaretVisible(bool visible)
    {
        _caretVisible = visible && TextBox != null && TextBox.hasFocus;
        ApplyScrolledText();
    }

    /// <summary>
    /// 現在の本文を折り返し計算し、_scrollLineを起点にした表示ウィンドウ分だけを
    /// _displayTextへ反映する。scrollToEnd=trueの時は末尾(最新の入力箇所)が
    /// 見えるようスクロール位置を自動で末尾へ合わせる。
    /// </summary>
    private void RefreshDisplay(bool scrollToEnd)
    {
        var full = _fullText;

        if (_placeholderText != null)
            _placeholderText.gameObject.SetActive(string.IsNullOrEmpty(full));

        if (!_multiline)
        {
            if (_displayText != null) _displayText.text = BuildTextWithCaret(full);
            return;
        }

        _wrappedLines = WrapLines(full);

        if (scrollToEnd)
        {
            _scrollLine = Mathf.Max(0, _wrappedLines.Length - _maxVisibleLines);
        }
        else
        {
            // カーソル位置が現在の表示範囲外に出た場合、カーソルが見える位置まで
            // スクロール位置を追従させる(矢印キーでの移動時に必要)。
            var caretLine = GetCaretLineIndex();
            if (caretLine < _scrollLine) _scrollLine = caretLine;
            else if (caretLine >= _scrollLine + _maxVisibleLines) _scrollLine = caretLine - _maxVisibleLines + 1;
        }
        _scrollLine = Mathf.Clamp(_scrollLine, 0, Mathf.Max(0, _wrappedLines.Length - 1));

        ApplyScrolledText();
    }

    /// <summary>_cursorIndexが、折り返し後の何行目に位置するかを求める。</summary>
    private int GetCaretLineIndex()
    {
        if (_wrappedLines.Length == 0) return 0;
        var remaining = _cursorIndex;
        for (var i = 0; i < _wrappedLines.Length; i++)
        {
            var lineLen = _wrappedLines[i].Length;
            if (remaining <= lineLen) return i;
            remaining -= lineLen;
            // この行の直後に実際の改行文字がある場合のみ、その1文字ぶんを消費する。
            // (自動折り返しの継ぎ目には文字は存在しないため消費しない)
            var hasNewline = i < _wrappedLineHasNewlineAfter.Length && _wrappedLineHasNewlineAfter[i];
            if (hasNewline) remaining -= 1;
        }
        return _wrappedLines.Length - 1;
    }

    /// <summary>
    /// 本文を、実際の文字幅(全角/半角)を考慮して折り返す。
    /// 半角文字は全角文字のおよそ半分の表示幅しかないため、固定文字数で折り返すと
    /// 「半角だけの行はまだ入るはずなのに早く折り返される(横幅が半分に見える)」
    /// 「全角だけの行は逆に枠からはみ出す」という不具合が起きていた。
    /// そのため、1文字ずつ幅を積算し、_charsPerLine(半角換算での上限文字数)に
    /// 相当する幅を超えたところで折り返す方式にする。
    /// あわせて、_wrappedLineHasNewlineAfterも同時に構築する
    /// (各行の直後が実際の改行文字なのか、自動折り返しの継ぎ目なのかを記録する)。
    /// </summary>
    private string[] WrapLines(string full)
    {
        if (string.IsNullOrEmpty(full))
        {
            _wrappedLineHasNewlineAfter = Array.Empty<bool>();
            return Array.Empty<string>();
        }

        var rawLines = full.Replace("\r\n", "\n").Split('\n');
        var result = new List<string>();
        var hasNewlineAfter = new List<bool>();
        // _charsPerLineは「半角文字換算での1行の上限文字数」。
        // 全角文字は、大きめに見て2文字分として計算すると全角だけの行が
        // 逆に半角換算の余裕を使い切れず、枠の右側が余ってしまうことがわかったため、
        // 実測に近い1.7文字分として計算する。
        var maxWidthUnits = _charsPerLine;

        for (var rawIndex = 0; rawIndex < rawLines.Length; rawIndex++)
        {
            var rawLine = rawLines[rawIndex];
            var isLastRawLine = rawIndex == rawLines.Length - 1;

            if (rawLine.Length == 0)
            {
                result.Add("");
                hasNewlineAfter.Add(!isLastRawLine);
                continue;
            }

            var lineStart = 0;
            var widthUnits = 0f;
            for (var i = 0; i < rawLine.Length; i++)
            {
                var charWidthUnits = IsFullWidth(rawLine[i]) ? 1.7f : 1f;
                if (widthUnits + charWidthUnits > maxWidthUnits && i > lineStart)
                {
                    result.Add(rawLine[lineStart..i]);
                    hasNewlineAfter.Add(false); // 自動折り返しの継ぎ目には実際の改行はない
                    lineStart = i;
                    widthUnits = 0;
                }
                widthUnits += charWidthUnits;
            }
            result.Add(rawLine[lineStart..]);
            // 元の生の行の最後の断片の直後だけ、実際の改行文字があるかどうかを反映する。
            hasNewlineAfter.Add(!isLastRawLine);
        }
        _wrappedLineHasNewlineAfter = hasNewlineAfter.ToArray();
        return result.ToArray();
    }

    /// <summary>大まかな全角判定(日本語・記号等、半角英数字/半角カナ以外は全角とみなす)。</summary>
    private static bool IsFullWidth(char c)
    {
        // 半角英数字・半角記号・半角スペース
        if (c is >= (char)0x20 and <= (char)0x7E) return false;
        // 半角カナ
        if (c is >= (char)0xFF61 and <= (char)0xFF9F) return false;
        return true;
    }

    /// <summary>非複数行入力欄用: カーソル位置に"|"を挿入した文字列を作る。</summary>
    private string BuildTextWithCaret(string full)
    {
        if (!_caretVisible) return full;
        var idx = Mathf.Clamp(_cursorIndex, 0, full.Length);
        return full[..idx] + "|" + full[idx..];
    }

    private void ApplyScrolledText()
    {
        if (_displayText == null) return;
        string shown;
        if (_wrappedLines.Length == 0)
        {
            shown = _caretVisible ? "|" : "";
            _displayText.text = shown;
            return;
        }

        var start = Mathf.Clamp(_scrollLine, 0, _wrappedLines.Length - 1);
        var end = Mathf.Min(_wrappedLines.Length, start + _maxVisibleLines);
        var lines = new List<string>(_wrappedLines[start..end]);

        if (_caretVisible)
        {
            // カーソルが今の表示範囲内にあれば、該当行の該当文字位置に"|"を挿入する。
            var caretLine = GetCaretLineIndex();
            if (caretLine >= start && caretLine < end)
            {
                var localLineIndex = caretLine - start;
                var caretColumn = GetCaretColumnInLine(caretLine);
                var line = lines[localLineIndex];
                caretColumn = Mathf.Clamp(caretColumn, 0, line.Length);
                lines[localLineIndex] = line[..caretColumn] + "|" + line[caretColumn..];
            }
        }

        _displayText.text = string.Join("\n", lines);
    }

    /// <summary>_cursorIndexが、指定した折り返し行の中で何文字目(列)に位置するかを求める。</summary>
    private int GetCaretColumnInLine(int targetLine)
    {
        var remaining = _cursorIndex;
        for (var i = 0; i < _wrappedLines.Length; i++)
        {
            var lineLen = _wrappedLines[i].Length;
            if (i == targetLine) return Mathf.Clamp(remaining, 0, lineLen);
            if (remaining <= lineLen) return remaining;
            remaining -= lineLen;
            var hasNewline = i < _wrappedLineHasNewlineAfter.Length && _wrappedLineHasNewlineAfter[i];
            if (hasNewline) remaining -= 1;
        }
        return 0;
    }

    /// <summary>マウスホイール操作等から呼ばれる、手動スクロール用API。delta&gt;0で下へ、&lt;0で上へ。</summary>
    public void ScrollBy(int deltaLines)
    {
        if (!_multiline || _wrappedLines.Length == 0) return;
        _scrollLine = Mathf.Clamp(_scrollLine + deltaLines, 0, Mathf.Max(0, _wrappedLines.Length - _maxVisibleLines));
        ApplyScrolledText();
    }

    public void SetText(string value)
    {
        value ??= "";
        _fullText = value;
        _cursorIndex = _fullText.Length;
        TextBox?.SetText("");
        if (TextBox?.outputText != null)
        {
            TextBox.outputText.text = "";
        }
        RefreshDisplay(scrollToEnd: false);
    }

    /// <summary>Shift+Enterでの改行挿入など、末尾への追記であることが分かっている場合に使う。
    /// 通常のSetTextと違い、末尾までスクロールする。</summary>
    internal void AppendText(string suffix)
    {
        InsertAtCursor(suffix, moveCursorToEnd: true);
    }

    /// <summary>カーソル位置に文字列を挿入する(途中編集の要)。</summary>
    internal void InsertAtCursor(string insert, bool moveCursorToEnd = false)
    {
        if (string.IsNullOrEmpty(insert)) return;
        if (_characterLimit > 0 && _fullText.Length >= _characterLimit) return;

        var idx = Mathf.Clamp(_cursorIndex, 0, _fullText.Length);
        var allowed = _characterLimit > 0 ? Mathf.Max(0, _characterLimit - _fullText.Length) : insert.Length;
        if (allowed < insert.Length) insert = insert[..allowed];
        if (insert.Length == 0) return;

        _fullText = _fullText[..idx] + insert + _fullText[idx..];
        _cursorIndex = idx + insert.Length;
        RefreshDisplay(scrollToEnd: moveCursorToEnd);
    }

    /// <summary>カーソルの前(Backspace)または後ろ(Delete)の1文字を削除する。</summary>
    internal void DeleteAtCursor(bool backward)
    {
        if (_fullText.Length == 0) return;

        if (backward)
        {
            if (_cursorIndex <= 0) return;
            _fullText = _fullText[..(_cursorIndex - 1)] + _fullText[_cursorIndex..];
            _cursorIndex -= 1;
        }
        else
        {
            if (_cursorIndex >= _fullText.Length) return;
            _fullText = _fullText[.._cursorIndex] + _fullText[(_cursorIndex + 1)..];
        }
        RefreshDisplay(scrollToEnd: false);
    }

    /// <summary>カーソルを左右に移動する。</summary>
    internal void MoveCursor(int delta)
    {
        _cursorIndex = Mathf.Clamp(_cursorIndex + delta, 0, _fullText.Length);
        RefreshDisplay(scrollToEnd: false);
    }

    /// <summary>カーソルを行頭・行末・先頭・末尾へ移動する。</summary>
    internal void MoveCursorToStart() { _cursorIndex = 0; RefreshDisplay(scrollToEnd: false); }
    internal void MoveCursorToEnd() { _cursorIndex = _fullText.Length; RefreshDisplay(scrollToEnd: false); }

    public void SetActive(bool active) => TextBox?.gameObject.SetActive(active);
}

/// <summary>
/// マウスホイールでのスクロール入力を拾い、対応するSimpleTextBoxへ伝える。
/// バグ内容欄のように長文になり得る複数行入力欄にだけ付与される。
/// </summary>
internal class TextBoxScrollHandler : MonoBehaviour
{
    public SimpleTextBox Owner;

    private void OnMouseOver()
    {
        var scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) < 0.01f) return;
        Owner?.ScrollBy(scroll > 0 ? -1 : 1);
    }
}

/// <summary>
/// 入力欄がフォーカスされている間、実際のテキスト編集(文字入力・削除・カーソル移動)を
/// すべて自前で処理する。TextBoxTMPは「末尾への追記・削除のみ」しか対応しておらず、
/// 文中への挿入や矢印キーでのカーソル移動を提供しないため、Input.inputStringと
/// 矢印キー・Backspace・Delete・Home・Endを直接監視して、SimpleTextBox側の
/// _fullText/_cursorIndexを操作することで「途中編集」を実現する。
/// あわせて、末尾カーソル("|")の点滅や、複数行入力欄でのShift+Enterによる
/// 改行入力もここで処理する。
/// </summary>
internal class TextBoxCaretBlinker : MonoBehaviour
{
    public SimpleTextBox Owner;
    private float _timer;
    private bool _visible;
    private const float BlinkInterval = 0.5f;

    // キーリピート(押しっぱなしでの連続移動・連続削除)用のタイマー。
    private float _repeatTimer;
    private KeyCode _repeatingKey = KeyCode.None;
    private const float RepeatInitialDelay = 0.4f;
    private const float RepeatInterval = 0.04f;

    private void Update()
    {
        if (Owner == null) return;

        var textBox = GetComponent<TextBoxTMP>();
        if (textBox == null || !textBox.hasFocus)
        {
            if (_visible)
            {
                _visible = false;
                Owner.SetCaretVisible(false);
            }
            _repeatingKey = KeyCode.None;
            return;
        }

        var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        var ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
            || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);

        // ===== Enter / Shift+Enter =====
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (Owner.IsMultiline && shift)
            {
                Owner.AppendText("\n");
                textBox.GiveFocus();
            }
            // 複数行でなければ何もしない(送信ボタンは別途押してもらう想定)。
        }

        // ===== 文字入力 =====
        // Ctrl(コピー・ペースト等のショートカット)併用時は文字入力として扱わない。
        var input = Input.inputString;
        if (!string.IsNullOrEmpty(input) && !ctrl)
        {
            foreach (var ch in input)
            {
                // 制御文字(Backspace='\b', Enter='\n'/'\r')は別処理に任せ、ここでは無視する。
                if (ch == '\b' || ch == '\n' || ch == '\r') continue;
                Owner.InsertAtCursor(ch.ToString());
            }
        }

        // ===== 矢印キー・Backspace・Delete・Home・End(押した瞬間 + リピート) =====
        HandleRepeatableKey(KeyCode.LeftArrow, () => Owner.MoveCursor(-1));
        HandleRepeatableKey(KeyCode.RightArrow, () => Owner.MoveCursor(1));
        HandleRepeatableKey(KeyCode.Backspace, () => Owner.DeleteAtCursor(backward: true));
        HandleRepeatableKey(KeyCode.Delete, () => Owner.DeleteAtCursor(backward: false));
        if (Owner.IsMultiline)
        {
            HandleRepeatableKey(KeyCode.UpArrow, () => Owner.MoveCursor(-1));
            HandleRepeatableKey(KeyCode.DownArrow, () => Owner.MoveCursor(1));
        }
        if (Input.GetKeyDown(KeyCode.Home)) Owner.MoveCursorToStart();
        if (Input.GetKeyDown(KeyCode.End)) Owner.MoveCursorToEnd();

        _timer += Time.deltaTime;
        if (_timer >= BlinkInterval)
        {
            _timer = 0f;
            _visible = !_visible;
            Owner.SetCaretVisible(_visible);
        }
    }

    /// <summary>指定キーの「押した瞬間」と「押しっぱなしでのリピート」を処理する共通ヘルパー。</summary>
    private void HandleRepeatableKey(KeyCode key, Action action)
    {
        if (Input.GetKeyDown(key))
        {
            action();
            _repeatingKey = key;
            _repeatTimer = 0f;
            return;
        }

        if (Input.GetKeyUp(key) && _repeatingKey == key)
        {
            _repeatingKey = KeyCode.None;
            return;
        }

        if (_repeatingKey == key && Input.GetKey(key))
        {
            _repeatTimer += Time.deltaTime;
            if (_repeatTimer >= RepeatInitialDelay)
            {
                // 初回ディレイを超えたら、RepeatInterval間隔で連続実行する。
                var over = _repeatTimer - RepeatInitialDelay;
                if (over >= RepeatInterval)
                {
                    _repeatTimer -= RepeatInterval;
                    action();
                }
            }
        }
    }
}
