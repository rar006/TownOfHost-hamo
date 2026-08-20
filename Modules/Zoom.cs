using HarmonyLib;

using UnityEngine;



namespace TownOfHost

{

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]

    public static class Zoom

    {

        // HudManager.Start前に型が読み込まれる場合でも例外にならないよう、カメラサイズは遅延初期化する。

        public static int size = 3;

        private static int last = 0;

        private static bool initialized;

        public static void Postfix()

        {

            var hud = HudManager.Instance;

            var player = PlayerControl.LocalPlayer;

            if (hud == null || hud.UICamera == null || player == null || player.Data == null || player.Data.Role == null) return;



            if (!initialized)

            {

                size = Mathf.RoundToInt(hud.UICamera.orthographicSize);

                last = size;

                initialized = true;

            }



            if ((GameStates.IsFreePlay && Input.GetKey(KeyCode.LeftAlt)) || (Options.UseZoom.GetBool() && GameStates.IsInGame && !player.IsAlive() && !player.IsGhostRole() && GameStates.IsInTask))

            {

                //チャットなど開いていて、動けない状態 なら操作を無効にする

                if (!player.CanMove) return;



                if (Input.mouseScrollDelta.y < 0) size += (int)1.5;

                if (Input.mouseScrollDelta.y > 0 && size > 1.5) size -= (int)1.5;

                if (Input.GetKeyDown(KeyCode.LeftShift)) size = 3;

            }

            else

                size = 3;



            //位置を調整

            if (last != size)

            {

                hud.UICamera.orthographicSize = size;

                if (Camera.main != null) Camera.main.orthographicSize = size;

                ResolutionManager.ResolutionChanged.Invoke((float)Screen.width / Screen.height, Screen.width, Screen.height, Screen.fullScreen);

                last = size;

            }

        }

    }

}