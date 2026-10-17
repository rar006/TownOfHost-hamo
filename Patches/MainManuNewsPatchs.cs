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
#if HAMO_ANDROID
        // Android では ref の配列を差し替える方法が不安定なので、下の Postfix(SetModAnnouncementsAndroid)で追加する
        if (true) return true;
#endif
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

#if HAMO_ANDROID && HAMO_ANDROID_MODNEWS
    // 実機(Pixel 10 / Android 17)で、この方法で追加した後にニュース画面を開くとネイティブクラッシュしたため無効化中。
    /// <summary>
    /// Android用: ゲームがニュースを読み込んだ後に、MODニュースを allAnnouncements へ追加して日付順に並べ直す。
    /// (配列を ref で差し替えないので、IL2CPPの相互運用で落ちにくい。失敗しても公式ニュースはそのまま)
    /// </summary>
    [HarmonyPatch(typeof(PlayerAnnouncementData), nameof(PlayerAnnouncementData.SetAnnouncements)), HarmonyPostfix]
    public static void SetModAnnouncementsAndroid(PlayerAnnouncementData __instance)
    {
        try
        {
            if (AllModNews.Count < 1)
            {
                Init();
                AllModNews.Do(n => JsonAndAllModNews.Add(n));
            }
            var list = __instance.allAnnouncements;
            if (list == null) return;
            var merged = new List<Announcement>();
            foreach (var a in list)
                if (a != null && !JsonAndAllModNews.Any(x => x.Number == a.Number)) merged.Add(a);
            JsonAndAllModNews.Do(n => merged.Add(n.ToAnnouncement()));
            merged.Sort((a1, a2) => DateTime.Compare(ParseDateSafe(a2.Date), ParseDateSafe(a1.Date)));
            list.Clear();
            foreach (var a in merged) list.Add(a);
            TownOfHost.Logger.Info($"ModNews(Android): {JsonAndAllModNews.Count}件を追加 (合計{list.Count}件)", "ModNews");
        }
        catch (Exception e)
        {
            TownOfHost.Logger.Error($"ModNews(Android)の追加に失敗: {e}", "ModNews");
        }
    }
#endif
}