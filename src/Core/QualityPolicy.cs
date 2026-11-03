using System;

namespace PartnerLive2D.Core
{
    public sealed class QualityPolicy
    {
        public readonly string Requested, Reason;
        public readonly bool Low;
        public int Slots => Low ? 32 : 80;
        public int Grid => Low ? 12 : 20;
        public int PlanAhead => Low ? 10 : 20;
        public int BetweenAhead => Low ? 0 : 10;
        public double UploadBudgetMs => Low ? 1.0 : 2.5;
        public int RenderSize(int configured) => Low ? Math.Min(configured, 1024) : configured;

        public QualityPolicy(string requested, int memoryMb, int processors)
        {
            Requested = (requested ?? "auto").Trim().ToLowerInvariant();
            bool invalid = Requested != "auto" && Requested != "high" && Requested != "low";
            if (invalid) Requested = "auto";
            Low = Requested == "low" || (Requested == "auto" && (memoryMb <= 2048 || processors <= 4));
            Reason = (invalid ? "invalid setting, using auto; " : "") +
                (Requested == "auto" ? "auto: VRAM " + memoryMb + " MB, processors " + processors +
                    " (low when VRAM <= 2048 MB or processors <= 4)" : "explicit " + Requested);
        }
    }
}
