using RimWorld;
using UnityEngine;
using Verse;

namespace Settlement_Services.UI
{
    public class Dialog_EmbryoGenes : Window
    {
        private readonly HumanEmbryo embryo;
        private Vector2 scrollPosition;

        public override Vector2 InitialSize => new Vector2(736f, 700f);

        public Dialog_EmbryoGenes(HumanEmbryo embryo)
        {
            this.embryo = embryo;
            closeOnClickedOutside = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            inRect.yMax -= Window.CloseButSize.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "SettlementServices.Label.EmbryoGenesTitle".Translate(embryo.LabelCap));
            Text.Font = GameFont.Small;
            inRect.yMin += 34f;

            Vector2 size = Vector2.zero;
            GeneUIUtility.DrawGenesInfo(inRect, embryo, InitialSize.y, ref size, ref scrollPosition);

            if (Widgets.ButtonText(new Rect(inRect.xMax - Window.CloseButSize.x, inRect.yMax, Window.CloseButSize.x, Window.CloseButSize.y), "Close".Translate()))
                Close();
        }
    }
}
