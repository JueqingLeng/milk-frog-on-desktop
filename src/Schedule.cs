using System;

namespace MilkyFrog
{
    public sealed class Activity
    {
        public string Sprite { get; private set; }
        public string Caption { get; private set; }
        public Activity(string sprite, string caption) { Sprite = sprite; Caption = caption; }
    }

    public static class Schedule
    {
        public static DateTime BeijingNow()
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"));
        }

        public static Activity At(int minutes)
        {
            if (minutes < 390 || minutes >= 1380) return new Activity("sleep", "23:00—06:30 · 睡觉中");
            if (minutes < 480) return new Activity("meal", "06:30—08:00 · 吃早餐");
            if (minutes < 700) return new Activity("study", "08:00—11:40 · 学习中");
            if (minutes < 780) return new Activity("meal", "11:40—13:00 · 吃午餐");
            if (minutes < 990) return new Activity("study", "13:00—16:30 · 学习中");
            if (minutes < 1080) return new Activity("meal", "16:30—18:00 · 吃晚餐");
            if (minutes < 1260) return new Activity("computer", "18:00—21:00 · 用电脑学习");
            return new Activity("game", "21:00—23:00 · 打游戏");
        }
    }
}
