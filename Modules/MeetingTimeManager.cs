using System;
using AmongUs.GameOptions;

using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using TownOfHost.Roles.Impostor;

namespace TownOfHost.Modules
{
    public class MeetingTimeManager
    {
        private static int DiscussionTime;
        private static int VotingTime;
        private static int DefaultDiscussionTime;
        private static int DefaultVotingTime;

        public static void Init()
        {
            DefaultDiscussionTime = Main.RealOptionsData.GetInt(Int32OptionNames.DiscussionTime);
            DefaultVotingTime = Main.RealOptionsData.GetInt(Int32OptionNames.VotingTime);
            Logger.Info($"DefaultDiscussionTime:{DefaultDiscussionTime}, DefaultVotingTime{DefaultVotingTime}", "MeetingTimeManager.Init");
            ResetMeetingTime();
        }
<<<<<<< HEAD
=======

>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
        public static void ApplyGameOptions(IGameOptions opt)
        {
            opt.SetInt(Int32OptionNames.DiscussionTime, DiscussionTime);
            opt.SetInt(Int32OptionNames.VotingTime, VotingTime);
        }
<<<<<<< HEAD
=======

>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
        private static void ResetMeetingTime()
        {
            DiscussionTime = DefaultDiscussionTime;
            VotingTime = DefaultVotingTime;
        }
<<<<<<< HEAD
        public static void OnReportDeadBody()
        {
            if (Roles.Crewmate.Balancer.Id != 255 && Roles.Crewmate.Balancer.target1 is not 255 && Roles.Crewmate.Balancer.target1 is not 255)
=======

        public static void OnReportDeadBody()
        {
            // ★ ニムロッド会議
            if (Roles.Crewmate.Nimrod.IsExecutionMeeting())
            {
                Nimrod(Roles.Crewmate.Nimrod.meetingtime);
                return;
            }

            // 天秤会議
            if (Roles.Crewmate.Balancer.Id != 255
                && Roles.Crewmate.Balancer.target1 is not 255
                && Roles.Crewmate.Balancer.target2 is not 255)
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
            {
                Balancer(Roles.Crewmate.Balancer.meetingtime);
                return;
            }
<<<<<<< HEAD
            if (Assassin.assassin?.NowState is Assassin.AssassinMeeting.Guessing or Assassin.AssassinMeeting.CallMetting)
=======

            if (Assassin.assassin?.NowState is Assassin.AssassinMeeting.Guessing
                or Assassin.AssassinMeeting.CallMetting)
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
            {
                DiscussionTime = 0;
                VotingTime = Assassin.OptionAssassinMeetingTime.GetInt();
                return;
            }
<<<<<<< HEAD
=======

>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
            if (Options.AllAliveMeeting.GetBool() && PlayerCatch.IsAllAlive)
            {
                DiscussionTime = 0;
                VotingTime = Options.AllAliveMeetingTime.GetInt();
                Logger.Info($"DiscussionTime:{DiscussionTime}, VotingTime{VotingTime}", "MeetingTimeManager.OnReportDeadBody");
                return;
            }

            ResetMeetingTime();
            int BonusMeetingTime = 0;
            int MeetingTimeMin = 0;
            int MeetingTimeMax = 300;
            MeetingTimeMin = Options.LowerLimitVotingTime.GetInt();
            MeetingTimeMax = Options.MeetingTimeLimit.GetInt();

            foreach (var role in CustomRoleManager.AllActiveRoles.Values)
            {
                if (role is IMeetingTimeAlterable meetingTimeAlterable)
                {
                    if (!role.Player.IsAlive() && meetingTimeAlterable.RevertOnDie)
<<<<<<< HEAD
                    {
                        continue;
                    }
=======
                        continue;
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56

                    var time = meetingTimeAlterable.CalculateMeetingTimeDelta();
                    Logger.Info($"会議時間-{role.Player.GetNameWithRole()}: {time} s", "MeetingTimeManager.OnReportDeadBody");
                    BonusMeetingTime += time;
                }
            }

            int TotalMeetingTime = DiscussionTime + VotingTime;
<<<<<<< HEAD
            //時間の下限、上限で刈り込み
            BonusMeetingTime = Math.Clamp(TotalMeetingTime + BonusMeetingTime, MeetingTimeMin, MeetingTimeMax) - TotalMeetingTime;
            if (BonusMeetingTime >= 0)
                VotingTime += BonusMeetingTime; //投票時間を延長
            else
            {
                DiscussionTime += BonusMeetingTime; //会議時間を優先的に短縮
                if (DiscussionTime < 0) //会議時間だけでは賄えない場合
                {
                    VotingTime += DiscussionTime; //足りない分投票時間を短縮
=======
            BonusMeetingTime = Math.Clamp(TotalMeetingTime + BonusMeetingTime, MeetingTimeMin, MeetingTimeMax) - TotalMeetingTime;
            if (BonusMeetingTime >= 0)
                VotingTime += BonusMeetingTime;
            else
            {
                DiscussionTime += BonusMeetingTime;
                if (DiscussionTime < 0)
                {
                    VotingTime += DiscussionTime;
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
                    DiscussionTime = 0;
                }
            }
            Logger.Info($"DiscussionTime:{DiscussionTime}, VotingTime{VotingTime}", "MeetingTimeManager.OnReportDeadBody");
        }

        public static void Balancer(int time)
        {
            DiscussionTime = 0;
            VotingTime = time;
        }
<<<<<<< HEAD
=======

        // ★ ニムロッド会議用（Balancer と同じ実装）
        public static void Nimrod(int time)
        {
            DiscussionTime = 0;
            VotingTime = time;
        }
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    }
}