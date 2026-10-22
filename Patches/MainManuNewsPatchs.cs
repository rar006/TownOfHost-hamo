//TOH_Yを参考にさせて貰いました ありがとうございます
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AmongUs.Data.Player;
using Assets.InnerNet;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Newtonsoft.Json.Linq;
using TownOfHost;
using UnityEngine.Networking;

[HarmonyPatch]
public class ModNewsHistory
{
    public static List<ModNews> AllModNews = new();
    public static List<ModNews> JsonAndAllModNews = new();
    public static void Init()
    {
        {
            //リンクはこうやるらしい。<nobr><link=\"URL\">Text</nobr></link>
            /*　　テンプレート
            {
                var news = new ModNews
                {
                    Number = 100002,
                    Title = "text",
                    SubTitle = "<color=#FF9631>Town Of Host-hamo v3.23.11.39</color>",
                    ShortTitle = "<color=#FF9631>●TOH-hamo v3.23.11.39</color>",
                    Text = "text\n"
                    + "・text\n"
                    + "・text\n"
                    + "・text\n"
                    + "・text\n"
                    + "・text\n"
                    + "・text\n"
                    + "・text\n"
                    + "・text\n"
                    + "・text\n"
                    + "・text\n"
                    + "・text\n"
                    + "・text"
                    ,
                    Date = "2026-4-20T00:00:00Z"
                };
                AllModNews.Add(news);
            }*/
            {
                var news = new ModNews
                {
                    Number = 100001,
                    Title = "新リリースだよ",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.00.00.00</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.00.00.00</color>",
                    Date = "2026-07-15T19:00:00Z",
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100002,
                    Title = "アプデしたよー",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.00.00.01</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.00.00.01</color>",
                    Date = "2026-7-18"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100003,
                    Title = "色々したから忘れちゃった★",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.00.00.10</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.00.00.10</color>",
                    Date = "2026-8-1"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100004,
                    Title = "Pkoアプデ対応。後は色々...",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.00.00.11</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.00.00.11</color>",
                    Date = "2026-7-18"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100005,
                    Title = "バグ修正したよん！",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.00.00.12</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.00.00.12</color>",
                    Date = "2026-7-19"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100006,
                    Title = "大型アプデだー！夏暑いね！",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.00.00.20</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.00.00.20</color>",
                    Text = "・TOHPkoのアプデに対応\r\n・新UI解放\r\n・プロフェショナル追加\r\n・マッドカウント追加\r\n・リバーサル追加\r\n・アステル追加\r\n・ダークシェリフ追加\r\n・アライグマ追加\r\n・シンボル追加\r\n・ラストクルーメイト追加\r\n・/cmd n rで部屋が落ちてしまうバグを修正\r\n・コマンド説明タブのバグ修正\r\n・設定画面が2回目開かなくなるバグ修正\r\n・GMの詳細設定追加\r\n"
                    ,
                    Date = "2026-8-19"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100007,
                    Title = "公式のアプデ来たね！猿の準備だ！",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.00.00.21</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.00.00.21</color>",
                    Text = "・TOHPkoのアプデに対応\r\n・TOHKのアプデ対応\r\n"
                    ,
                    Date = "2026-8-20"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100008,
                    Title = "久しぶりのリリースだね..汗",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.00.31α</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.00.31α</color>",
                    Text = "・リザルター追加\r\n・トゥモロー追加\r\n・リピーター追加\r\n・コレクシス追加\r\n・マエストロ追加\r\n・その他諸々..NEWついているからここでは省略するね★"
                    ,
                    Date = "2026-9-26"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100009,
                    Title = "大型アップデート！新役職たくさん！",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.35.00β</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.35.00β</color>",
                    Text = "【新役職・新属性】\r\n・トラウマ(インポスター)\r\n・サーマウント(クルーメイト)\r\n・記憶復元者(クルーメイト)\r\n・ヴィラン(インポスター)(インポスター)\r\n・孤独な狼(インポスター)\r\n・クロックワーカー(インポスター)\r\n・アブダクター(インポスター)\r\n・マッドインビジブル(マッドメイト)\r\n・マッドマジック(マッドメイト)\r\n・カレイ(第三陣営)\r\n・ルパン(+アルセーヌ)(第三陣営)\r\n・ダオシー(+キョンシー)(第三陣営・10月限定)\r\n・サバイビング(属性)\r\n・バーサーカー(属性)\r\n・タフガイ(属性)\r\n・共存症(属性)\r\n・マーダー(属性)\r\n・カタリスト(+コンダクター)(第三陣営・コンビ)\r\n・センダー(クルーメイト)\r\n・マッドセンダー(マッドメイト)\r\n・パリティ(第三陣営)\r\n・ベトレイゲッサー(属性)\r\n\r\n【変更・修正】\r\n・追放されたプレイヤーが死亡扱いにならず、生き返ってしまう不具合を修正\r\n・試合中にMOD未導入の人が落ちることがある不具合を修正\r\n・自動立て直しが「設定を確認」の画面で止まる不具合を修正\r\n・人数ごとの設定で、Among Us側のインポスター数も合わせるように\r\n・NEWの役職を、役職一覧の先頭の「NEW」の項目にまとめるように\r\n・「限定役職」の項目をなくしました(期間限定の役職は五十音の行に並びます)\r\n・属性・ゴースト役職の一覧も五十音+NEWで並ぶように\r\n・役職の見出しを最初から開いておく設定を追加(クライアント設定)\r\n・サーバーの接続先を更新\r\n・二つ名に属性(マーク)が付くように\r\n・Discordからロビーにメッセージが届くように\r\n\r\n詳しくは各役職の説明を見てね★"
                    ,
                    Date = "2026-10-16"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100010,
                    Title = "アップデート！",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.35.01β</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.35.01β</color>",
                    Text = "【新役職】\r\n・エクスペリンサー(インポスター)\r\n\r\n【追加】\r\n・初手会議でチャットを無効化する設定\r\n・二つ名の引き換えコード(/cmd code コード)\r\n・二つ名の交換で、相手を色や番号で指定できるように\r\n・役職の見出しを全部ひらく・とじるボタン\r\n\r\n【修正】\r\n・試合後にロビーへ戻るとキックされることがある不具合\r\n・スリーパーが勝利できない不具合\r\n・ブラックビジョナーがマッドを作ると全員の色が変わる不具合\r\n・バグ報告などで落ちることがある不具合\r\n・Hボタンで別の役職の説明が出る不具合\r\n・その他、多少のバグ修正"
                    ,
                    Date = "2026-10-17"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100011,
                    Title = "アップデート！",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.35.02β</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.35.02β</color>",
                    Text = "【追加】\r\n・バグ報告の一覧に、対応状況(未対応・修正中・修正済み・既読)が表示されるように\r\n\r\n【変更・修正】\r\n・役職の見出しを全部ひらく・とじるボタンの大きさを、上のアイコンと揃えました\r\n・その他、多少のバグ修正"
                    ,
                    Date = "2026-10-17"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100012,
                    Title = "アップデート！",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.35.03β</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.35.03β</color>",
                    Text = "【追加】\r\n・バグ報告の専用チャットでも画像・動画を添付できるように\r\n・バグ報告の添付は一度に3つまで送れるように\r\n・二つ名のコマンドを /cmd なしでも使えるように(/nil など)\r\n・部屋ごとに二つ名を使うかどうか決められる設定(OTHER2)\r\n・Discordから全体や特定の部屋にメッセージを送れるように\r\n\r\n【修正】\r\n・キューピッドラバーズなどラバーズの勝利で「＆キューピッド勝利」のように名前が抜ける不具合\r\n・/n r で同じ陣営の見出しが2回出る不具合\r\n・ドッペルゲンガーの変身が止まることがある不具合\r\n・BANした人が入ってきた時にエラーが出る不具合\r\n・全部ひらく・とじるボタンが横長になっていた不具合\r\n・その他、多少のバグ修正"
                    ,
                    Date = "2026-10-17"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100013,
                    Title = "アップデート！",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.35.04β</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.35.04β</color>",
                    Text = "【追加】\r\n・役職ごとに初期キルクールを設定できるように(キルができる役職すべて)\r\n・YouTubeのコメントを画面の枠に表示できるように(ドラッグで移動・ゲーム内の設定からURLやON/OFF)\r\n・バージョン切り替えで過去のバージョンも選べるように(説明つき)\r\n・バグ報告は「既読」になると追加で報告できるように\r\n\r\n【変更】\r\n・初手会議でチャットを止めている間は「〇〇が投票しました」を出さないように\r\n\r\n【修正】\r\n・部屋を作ると勝手に試合が始まって壊れることがある不具合(自動スタート)\r\n・試合開始時に未導入の人がまとめて落ちることがある不具合\r\n・ホストの回線が一瞬途切れただけで部屋ごと落ちやすかった問題\r\n・ジャッジの判決が効かない不具合\r\n・リモートキラーで死んでいる人がもう一度キルされることがある不具合\r\n・ホストが死んでいる時、秘匿チャットのあとホストの名前が戻らない不具合\r\n・その他、多少のバグ修正"
                    ,
                    Date = "2026-10-17"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100014,
                    Title = "大型アップデート！便利機能たくさん！",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.35.05β</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.35.05β</color>",
                    Text = "【新役職】\r\n・歩爆魔(インポスター)\r\n・鍵師(クルーメイト)\r\n・検死官(クルーメイト)\r\n\r\n【便利機能】(OTHER2の「便利機能」でON/OFF)\r\n・同じ役職が続かないようにする\r\n・投票の残り時間を知らせる\r\n・入ってきた人に部屋のルールを送る\r\n・放置している人を知らせる\r\n・設定を変えたらチャットで知らせる\r\n・試合が終わったらMVPを知らせる\r\n・役職の勝率(/stats)・自分の戦績(/me)\r\n・初めての人にマーク\r\n・YouTubeのコメントでマップ投票\r\n\r\n【配信向け】\r\n・OBSに試合の状況を出せるように(Streamメニュー)\r\n・YouTubeコメント枠の設定をOTHER2とStreamメニューに移動\r\n\r\n【設定画面】\r\n・全役職の出現率をまとめて変える・おまかせ・人数に合ったおすすめ設定のタイル\r\n\r\n【修正】\r\n・YouTubeのコメントが表示されない不具合\r\n・ヴィランが試合前に死ぬ不具合\r\n・システムメッセージを送っている間、ホストがチャットできない不具合\r\n・その他、多少のバグ修正"
                    ,
                    Date = "2026-10-18"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100015,
                    Title = "アップデート！",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.35.06β</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.35.06β</color>",
                    Text = "【便利機能】(OTHER2の「便利機能」でON/OFF)\r\n・連勝を知らせる(連勝を止めた人はMVPの点数アップ)\r\n・Discordの共有ブラックリストを使う(登録された人を自動でキック)\r\n・設定を自動でバックアップする(「1つ前の設定に戻す」で戻せる)\r\n\r\n【配信向け】\r\n・YouTubeのURLにチャンネルのURLを入れられるように(配信中の動画を自動で探す)\r\n・コメントが取れない時は理由を枠に表示するように\r\n\r\n【修正】\r\n・YouTubeのコメントが表示されない不具合(配信が終わったURLのまま止まっていた)\r\n・ランチャーをインストールしても、インストール画面しか開かない不具合\r\n・回線が少し不安定なだけで「サーバー接続が切れました」になりやすかった問題を軽減\r\n・その他、多少のバグ修正"
                    ,
                    Date = "2026-10-18"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100016,
                    Title = "接続の安定化アップデート",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.35.07β</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.35.07β</color>",
                    Text = "【修正】\r\n・回線が少し不安定なだけで「サーバー接続が切れました」になりやすかった問題を軽減\r\n(返事を待つ時間を延ばし、部屋に入る前や接続が作り直された時にも効くように)\r\n・その他、多少のバグ修正"
                    ,
                    Date = "2026-10-18"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100017,
                    Title = "アップデート！",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.35.08β</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.35.08β</color>",
                    Text = "【配信向け】\r\n・YouTubeのコメント枠の大きさを変えられるように(右下の◢をドラッグ)\r\n・YouTubeのコメント枠を最小化できるように(右上の－/＋)\r\n\r\n【修正】\r\n・アサインモードがランダムの時、役職が付かない人が出ることがある不具合\r\n・最少を最大より大きくすると配役されない不具合\r\n・役職の設定に「最大数」が2つ出ることがある不具合\r\n・その他、多少のバグ修正"
                    ,
                    Date = "2026-10-18"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100018,
                    Title = "二つ名のSHOPが登場！",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.35.09β</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.35.09β</color>",
                    Text = "【二つ名】\r\n・かぶった二つ名は個数が増えるように(1000個=1kのように省略して表示)\r\n・二つ名を売って hm(通貨)にできるように(/cmd nis)\r\n・hm でSHOPの二つ名を買えるように(/cmd nip で見る・/cmd nib で買う)\r\n・もう持っている二つ名とも交換できるように\r\n・イベント中は引くクールタイムが短くなることがあります\r\n\r\n【便利機能】(OTHER2の「便利機能」でON/OFF)\r\n・ロビー投票(/vote でプリセット・/poll で自由な質問)\r\n・人数でプリセットを自動で切り替える\r\n\r\n【変更】\r\n・ジャッカルウルフの付与する役職を2つ設定できるように(試合ごとにランダムでどちらか)\r\n・/h に二つ名の売る・SHOPのコマンドを追加\r\n\r\n【修正】\r\n・その他、多少のバグ修正"
                    ,
                    Date = "2026-10-18"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100019,
                    Title = "アップデート！",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.35.10β</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.35.10β</color>",
                    Text = "【便利機能】(OTHER2の「便利機能」)\r\n・Discordからのメッセージ(/say)を受け取るかをON/OFFできるように(最初はON)\r\n　OFFでも、運営が「強制」で送ったメッセージは表示されます\r\n\r\n【修正】\r\n・配布版がデバッグ版の扱いになっていた問題\r\n・その他、多少のバグ修正"
                    ,
                    Date = "2026-10-18"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100020,
                    Title = "アップデート！",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.35.11β</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.35.11β</color>",
                    Text = "【二つ名】\r\n・属性ごとに分けて持つように(★付き・属性なしなどは別々に数える)\r\n・出にくい二つ名ほど高く売れるように(配布のみは10000hm・属性付きは×〇倍)\r\n・/cmd nii に売値を表示\r\n\r\n【修正】\r\n・死んでいる時(ゴースト中)にホストがチャットを開けない不具合\r\n・その他、多少のバグ修正"
                    ,
                    Date = "2026-10-18"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100021,
                    Title = "アップデート！",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.35.12β</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.35.12β</color>",
                    Text = "【追加】\r\n・チャットで記号(★♦♥!?#% など)を打てるように(自分の入力欄だけ)\r\n\r\n【変更】\r\n・インポスターのヴィランを「ヴィラン(インポスター)」に名前を変更\r\n\r\n【修正】\r\n・シュレ猫(マッド陣営)がリアクターなどのサボタージュで勝てない不具合\r\n・/h r で一途な狼を見ると孤独な狼が出る不具合\r\n・/h r でアルセーヌ・キョンシーを見ると顔文字が出る不具合\r\n・バージョン切り替え画面にロゴやボタンが重なることがある不具合\r\n・その他、多少のバグ修正"
                    ,
                    Date = "2026-10-18"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100022,
                    Title = "大型アップデート！",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.35.13β</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.35.13β</color>",
                    Text = "【追加】\r\n・参加者もhamoを入れたまま、hamoの部屋に入れるように(同じDLLでホストも参加もできる)\r\n・カジノ(/cmd nic: コイン・サイコロ・スロットで hm を賭ける。MOD無しの人も使える)\r\n・バグ報告で「みんなの報告」を見られるように(やり取りを見て、チャット・画像も送れる)\r\n\r\n【役職】\r\n・トゥモロー: 特殊会議で公開するのは、そのターンに死んだ人だけに\r\n・記憶喪失者: 思い出した後のキルクールを設定できるように\r\n・ダオシー: 自分がキルした死体はキョンシーにできない設定を追加\r\n・リバーサル: ベントでも逆転会議を開ける設定を追加(条件を満たしていないと自殺)\r\n・ファラオ: マークをファラオと死んだ人にだけ見せる設定を追加\r\n・アンチヒーロー: キルしなかった時のクルー追加勝利をON/OFFできるように\r\n\r\n【変更】\r\n・YouTubeコメント枠の「表示する」を普通のON/OFFに\r\n・チャットで記号を打てる機能を取りやめ(一部の文字が打てなくなっていたため)\r\n・メインメニューの赤い×ボタンを右下に移動・いつも表示\r\n\r\n【修正】\r\n・大きすぎるデータを送って「サーバー接続が切れました」になる問題を軽減(分けて送る・名前を切り詰める)\r\n・参加してすぐに切断された時、1回だけ自動で入り直すように\r\n・試合中に二つ名が表示されてしまうことがある不具合\r\n・テレポーターがペットで何度でも使えた不具合\r\n・設定画面のタイル(人数におすすめ など)が他の画面の上に出る・残る不具合\r\n・役職説明を開くとゲーム・プリセットが消える不具合\r\n・バグ報告の一覧で、押したものと違う報告が開くことがある不具合\r\n・その他、多少のバグ修正"
                    ,
                    Date = "2026-10-19"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100023,
                    Title = "アップデート！",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.35.14β</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.35.14β</color>",
                    Text = "【追加】\r\n・設定の自動バックアップ(設定やプリセットを変える前の状態を最大30件まで自動で保存。ゲーム設定の「設定のバックアップ」から一覧を開いて戻せる)\r\n・タイトルのロゴが、起動して最初の1回だけ1文字ずつ飛んできてバウンドするように\r\n・ロード画面で、MODのボタンが床でバウンドしながら流れるように\r\n・ロビーのTabの設定表示に、何も表示しないページを追加\r\n\r\n【役職】\r\n・出現率100%の役職は確定で出るように(枠より多い時は100%の役職の中からランダム)\r\n・悪役の狼: 覚醒時に付与する属性を一覧から選べるように(複数選ぶと全部付く)\r\n・アルカナ: 「自分をアルカナと自覚する」設定を追加\r\n\r\n【変更】\r\n・メインメニューの赤い×ボタンを画面の右下の角に\r\n・ロード画面の最終確認を3秒に\r\n\r\n【修正】\r\n・メインメニューの赤い×ボタンが、持ち物を開いて戻るまで表示されない不具合\r\n・ロビーのTabの設定表示が出ない・Tabを押すとエラーになる不具合\r\n・役職を選ぶ画面を開くと、検索欄やタイルなどが上に重なって見える不具合\r\n・その他、多少のバグ修正"
                    ,
                    Date = "2026-10-20"
                };
                AllModNews.Add(news);
            }
            {
                var news = new ModNews
                {
                    Number = 100024,
                    Title = "新役職追加！",
                    SubTitle = "<color=#fb85ff>Town Of Host-hamo v4.40.00α</color>",
                    ShortTitle = "<color=#fb85ff>●TOH-hamo v4.40.00α</color>",
                    Text = "【追加】\r\n・新しいバージョンが出たら、Among Usの中で知らせるように(OTHER2でON/OFF)\r\n・モデレーターの /cmd setting\r\n・確率1%以下の二つ名を引くと、ガードの演出が出るように(ロビーだけ)\r\n・バグ報告の入力欄に、書く項目のテンプレートが入るように\r\n\r\n【新役職】\r\n・フレーマー(インポスター): 近くの人を、選んだ人の見た目に変える\r\n・ナイスハッカー(クルー): ペットでハックモードにして、アドミンを見る\r\n・ロミオとジュリエット(第三陣営): 片方が死んだ後、残った方も死ぬと2人で勝利。/rj で秘匿チャット\r\n・マッドカインド(マッド): タスクを終えるとクルーとして勝てる\r\n\r\n【変更】\r\n・イーターをリストラ\r\n・キョンシーの説明に設定を出すように\r\n\r\n【修正】\r\n・参加者の回線が詰まってpingが不安定になる・切断される問題を軽減\r\n・廃村後、MOD無しの参加者が部屋から追い出されることがある不具合\r\n・アライグマの子・スタンドが設定していないのに配られる不具合\r\n・会議で名前の所に見出しが残ることがある不具合\r\n・ビジョンが0xだと試合開始時に真っ暗になる不具合\r\n・ホストがナイスハッカーの時、名前の下にずっと部屋が出ていた不具合\r\n・イビルリンカーのキルクールが、分身を置くたびに増える不具合\r\n・鍵師が封鎖したベントに何度も入ると、ベントから出られなくなる不具合\r\n・アライグマ以外のキル人外が全滅すると試合が終わり、アライグマが勝てなかった不具合\r\n・アブダクターが、ベントの上にいる人を引きずれない不具合\r\n・バグ報告で、添付するファイルを選ぶ画面がゲームの後ろに隠れて、再起動するまで添付できなくなることがある不具合\r\n・その他、多少のバグ修正"
                    ,
                    Date = "2026-10-22"
                };
                AllModNews.Add(news);
            }
            AnnouncementPopUp.UpdateState = AnnouncementPopUp.AnnounceState.NotStarted;
        }
    }
    //ここもTownOfHost_Y様を参考に..!
    public const string ModNewsURL = "";
    static bool downloaded = false;

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start)), HarmonyPostfix]
    public static void StartPostfix(MainMenuManager __instance)
    {
        static IEnumerator FetchModNews()
        {
            if (downloaded)
            {
                yield break;
            }
            // ===== 空URL/例外への対策 =====
            // ModNewsURLが空文字のままだと、UnityWebRequest.Get("")や後続のJSON解析が
            // 例外を投げてMainMenuManager.Startの実行タイミングに悪影響を与える可能性がある
            // (実際にログでMissingMethodExceptionが確認されている)。
            // URLが未設定の場合はそもそも何もしないようにする。
            if (string.IsNullOrWhiteSpace(ModNewsURL))
            {
                yield break;
            }
            downloaded = true;
            var request = UnityWebRequest.Get(ModNewsURL);
            yield return request.SendWebRequest();
            if (request.isNetworkError || request.isHttpError)
            {
                downloaded = false;
                TownOfHost.Logger.Info("ModNews Error Fetch:" + request.responseCode.ToString(), "ModNews");
                yield break;
            }
            JObject json;
            try
            {
                json = JObject.Parse(request.downloadHandler.text);
            }
            catch (Exception e)
            {
                // JSON解析に失敗しても、ここで完結させて他の処理に影響を与えないようにする。
                downloaded = false;
                TownOfHost.Logger.Info("ModNews Parse Error:" + e.Message, "ModNews");
                yield break;
            }
            for (var news = json["News"].First; news != null; news = news.Next)
            {
                JsonModNews n = new(
                    int.Parse(news.GetJ("Number").ToString()), news.GetJ("Title")?.ToString(), news.GetJ("Subtitle")?.ToString(), news.GetJ("Short")?.ToString(),
                    news.GetJ("Body")?.ToString(), news.GetJ("Date")?.ToString());
            }
        }
        __instance.StartCoroutine(FetchModNews().WrapToIl2Cpp());
    }

    private static DateTime ParseDateSafe(string date)
    {
        if (!string.IsNullOrEmpty(date) && DateTime.TryParse(date, out var result))
            return result;
        return DateTime.MinValue;
    }

    [HarmonyPatch(typeof(PlayerAnnouncementData), nameof(PlayerAnnouncementData.SetAnnouncements)), HarmonyPrefix]
    public static bool SetModAnnouncements(PlayerAnnouncementData __instance, [HarmonyArgument(0)] ref Il2CppReferenceArray<Announcement> aRange)
    {
        if (AllModNews.Count < 1)
        {
            Init();
            AllModNews.Do(n => JsonAndAllModNews.Add(n));
            JsonAndAllModNews.Sort((a1, a2) => { return DateTime.Compare(ParseDateSafe(a2.Date), ParseDateSafe(a1.Date)); });
        }

        List<Announcement> FinalAllNews = new();
        JsonAndAllModNews.Do(n => FinalAllNews.Add(n.ToAnnouncement()));
        foreach (var news in aRange)
        {
            if (!JsonAndAllModNews.Any(x => x.Number == news.Number))
                FinalAllNews.Add(news);
        }
        FinalAllNews.Sort((a1, a2) => { return DateTime.Compare(ParseDateSafe(a2.Date), ParseDateSafe(a1.Date)); });

        aRange = new(FinalAllNews.Count);
        for (int i = 0; i < FinalAllNews.Count; i++)
            aRange[i] = FinalAllNews[i];

        return true;
    }

}