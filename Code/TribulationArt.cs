using UnityEngine;

namespace Sakuga
{
    // Extends the existing pixel-art effect system. No terrain damage, no global flash.
    internal static class TribulationArt
    {
        private static Sprite _cloud;
        private static readonly Color Violet = new Color(.58f, .35f, 1f);
        private static readonly Color Blue = new Color(.45f, .80f, 1f);
        public static Sprite Cloud
        {
            get
            {
                if (_cloud != null) return _cloud;
                Canvas c = new Canvas(80, 32);
                for (int y = 0; y < 32; y++)
                for (int x = 0; x < 80; x++)
                {
                    float edge = 10f + 3f * Mathf.Sin(x * .18f) + 2f * Mathf.Sin(x * .39f);
                    float dy = Mathf.Abs(y - 15f);
                    float dx = (x - 40f) / 40f;
                    if (dy > edge * Mathf.Sqrt(Mathf.Max(0, 1f - dx * dx))) continue;
                    float shade = y > 16 ? .90f : .54f;
                    if ((x + y * 3) % 7 == 0) shade *= .85f;
                    c.Set(x, y, shade, 1f);
                }
                _cloud = c.ToSprite("sakuga_tribulation_cloud", new Vector2(.5f, .5f));
                return _cloud;
            }
        }

        public static void Warning(Vector2 at, float px, int round, float life)
        {
            Fx.Ring(at, px, life, 14f + round, 6f, 1, 2, FxLayer.Ground,
                8, Art.Ground, 1f, Fx.WithA(Violet, .15f), Blue, Fx.WithA(Color.white, .6f));
        }

        public static void Ambient(Vector2 at, float px, int round)
        {
            if (!Kit.Visible(at, 2f)) return;
            Vector2 sky = at + Vector2.up * 11f;
            Vector2 a = sky + new Vector2(Random.Range(-4f, 2f), Random.Range(-.8f, .8f));
            PrimFx.Bolt(a, a + new Vector2(Random.Range(1f, 3f), -.5f), px, .16f,
                Violet, FxLayer.Top, 37, false, 0);
            if (round >= 7)
                Fx.Ring(at, px, .7f, 6, 18, 1, 1, FxLayer.Ground, 5,
                    Art.Ground, 1f, Fx.WithA(Blue, .35f), Fx.WithA(Violet, 0));
        }

        public static void Strike(Actor actor, Vector2 at, float px, int round)
        {
            // Purely visual native lightning: don't call MapBox.spawnLightning*, which damages the map.
            EffectsLibrary.spawnAtTile(round <= 3 ? "fx_lightning_small" : round <= 6
                ? "fx_lightning_medium" : "fx_lightning_big", actor.current_tile,
                round == 9 ? 1.8f : .8f + round * .07f);
            if (Settings.TribulationEffects)
            {
                int columns = round == 9 ? 3 : 1;
                for (int i = 0; i < columns; i++)
                {
                    Vector2 from = at + new Vector2((i - (columns - 1) * .5f) * 2f, 11f);
                    PrimFx.Bolt(from, at, px * (1f + round * .16f), .20f + round * .025f,
                        round >= 7 ? Violet : Blue, FxLayer.Top, 40, false, round >= 4 ? 2 : 1);
                }
                Fx.Ring(at, px, .65f, 3, 15f + round * 3, 2, 1, FxLayer.Ground, 10,
                    Art.Ground, 2f, Color.white, Violet, Fx.WithA(Blue, 0));
                Particle dust = Fx.Spawn(Art.Dust[1], at, px * (2f + round * .2f), .8f, FxLayer.Over, 8);
                dust?.Color(Fx.WithA(new Color(.32f, .28f, .38f), .7f), Color.clear);
            }
        }

        public static void Victory(Vector2 at, float px)
        {
            Fx.Ring(at, px, 1.4f, 4, 70, 2, 1, FxLayer.Ground, 12,
                Art.Ground, 2f, Color.white, new Color(1f, .85f, .4f), Color.clear);
            PrimFx.Bolt(at + Vector2.up * 13f, at, px * 2, .6f,
                new Color(1f, .85f, .4f), FxLayer.Top, 40, false, 0);
        }
    }
}
