using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// HALL OF MIRRORS (Phantom Step, God). The God turns once on its heel, a silver ring flashing off
    /// it, and mirror images of itself glide out across a wide area - see-through copies with a sheen
    /// running over them - one coming to stand before every enemy, others among them as decoys. The
    /// God itself is one more image among them: nothing touches it while the hall stands. Each enemy,
    /// faced with it, strikes - a white swing - and the image it struck cracks... and shatters: shards
    /// of glass burst out and cut everyone of its enemies near it. The images go one after another,
    /// a cascade sweeping outward through the crowd.
    ///
    /// Levels (Gods always III; I-II through a bare trait): I six images within 7 tiles (shards x1 to
    /// those within 1.2 tiles); II ten within 9 (x1.3, 1.35 tiles); III fourteen within 11 (x1.6, 1.5
    /// tiles), and last of all the God's own body shatters too (it was a mirror as well) and it stands
    /// up out of the shards: everyone near it is cut. Damage x the user's damage, enemies only.
    /// </summary>
    internal sealed class HallOfMirrorsTech : Technique
    {
        public override string Id => "hall_of_mirrors";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Area;
        public override School School => School.PhantomStep;
        public override Form Forms => Form.Clone | Form.Area | Form.Evasive;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 110f, 150f, 200f };
        public override float[] Cooldown => new[] { 40f, 42f, 45f };

        private static int Mirrors(int level)
        {
            return level <= 1 ? 6 : level == 2 ? 10 : 14;
        }

        private static float Radius(int level)
        {
            return level <= 1 ? 7f : level == 2 ? 9f : 11f;
        }

        private static float Shard(int level)
        {
            return level <= 1 ? 1f : level == 2 ? 1.3f : 1.6f;
        }

        private static float ShardR(int level)
        {
            return level <= 1 ? 1.2f : level == 2 ? 1.35f : 1.5f;
        }

        private static readonly List<Actor> _scratch = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            float k = Choreo.KOf(c.Self);
            float r = Radius(level) * Mathf.Lerp(1f, k, 0.5f) + (c.Forced ? 3f : 0f);
            int mirrors = Mirrors(level);
            List<Actor> list = _scratch;
            list.Clear();
            for (int i = 0; i < c.Enemies.Count && i < 28 && list.Count < mirrors; i++)
            {
                if (c.EnemyDist[i] > r)
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (Choreo.Fair(c.Self, v, out _, c.Forced) && PsHiKit.Standing(v))
                {
                    list.Add(v);
                }
            }
            int need = c.Forced ? 1 : c.Surrounded || c.Linked ? 2 : 3;
            if (list.Count < need)
            {
                return null;
            }
            float score = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                Actor v = list[i];
                c.Allowed(v, out float cm);
                // its own image's shards, and some of its neighbours'
                int near = c.CountNear(v.current_position, ShardR(level) + 0.5f);
                score += c.Value(v, c.Expected(v, Shard(level) * (1f + 0.3f * Mathf.Min(2, near - 1))), cm);
            }
            if (c.Surrounded)
            {
                score += 30f;   // and nothing touches it while the hall stands
            }
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            return new Plan
            {
                Target = list[0],
                Point = c.Pos,
                Dir = PsHiKit.DirTo(c.Pos, list[0].current_position),
                Count = list.Count,
                Score = score,
                Others = new List<Actor>(list),
                AreaRadius = r,
                Data = c.Forced
            };
        }

        /// <summary>One hall: the images, where each stands, which enemy faces it, when it goes.</summary>
        private sealed class State
        {
            public Clone[] Images = new Clone[0];
            public readonly List<Vector2> Spots = new List<Vector2>(14);
            public readonly List<Actor> Facing = new List<Actor>(14);
            public readonly List<float> Breaks = new List<float>(14);
            public readonly List<bool> Swung = new List<bool>(14);
            public Particle Veil;
            public float T, SheenT;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            int mirrors = Mirrors(level);
            float r = Radius(level) * Mathf.Lerp(1f, m.Kk, 0.5f);
            State st = new State { Images = new Clone[mirrors] };
            Vector2 c0 = m.Stance;
            // where the images stand: before each enemy (on the God's side of it), the rest as decoys among them
            List<Actor> foes = p.Others ?? new List<Actor>();
            for (int i = 0; i < mirrors; i++)
            {
                Actor v = i < foes.Count ? foes[i] : null;
                Vector2 spot;
                if (v != null && Combat.Living(v) && PsHiKit.SpotBeside(v, c0, 0.75f * m.Kk + v.stats["size"] * 0.4f, out spot))
                {
                    st.Facing.Add(v);
                }
                else
                {
                    v = null;
                    float ang = Random.value * Mathf.PI * 2f;
                    float d = Random.Range(0.35f, 0.8f) * r;
                    if (!PsHiKit.SpotAround(c0, ang, d, out spot))
                    {
                        spot = c0;
                    }
                    st.Facing.Add(null);
                }
                st.Spots.Add(spot);
                st.Swung.Add(v == null);
            }
            // the cascade sweeps outward: the nearest images go first
            float[] order = new float[mirrors];
            for (int i = 0; i < mirrors; i++)
            {
                order[i] = Vector2.Distance(c0, st.Spots[i]) + (st.Facing[i] == null ? 1.5f : 0f);
            }
            float far = 0.1f;
            for (int i = 0; i < mirrors; i++)
            {
                far = Mathf.Max(far, order[i]);
            }
            for (int i = 0; i < mirrors; i++)
            {
                float glide = 0.14f + 0.012f * Vector2.Distance(c0, st.Spots[i]);
                float hold = 0.5f + 0.75f * order[i] / far + Random.Range(0f, 0.06f);
                st.Breaks.Add((glide + hold) * m.ActK);
            }
            Form shows = Form.Clone | Form.Area | Form.Evasive;
            // it turns once on its heel, a silver ring flashing off it
            m.SpinInPlace(1f, 0.3f).Untouchable().OnEnter(mm =>
            {
                if (mm.Vis)
                {
                    Fx.Ring(mm.Stance, mm.Px, 0.3f, 3f, 18f * mm.Kk, 2, 1, FxLayer.Ground, 5, Art.Ground, 2.4f, Fx.WithA(Color.white, 0.95f), Fx.WithA(mm.Hue.Edge, 0f));
                    Fx.Glint(mm.HeadPos(), mm.Px, 0.2f, Color.white, 1.1f);
                }
                Choreo.Sound("event:/SFX/HIT/HitMetal", mm.A.current_tile);
            });
            m.Last.Forms = shows;
            // the images glide out across the hall
            m.Clones(mirrors, 0.3f, (mm, c, i) =>
            {
                float glide = 0.14f + 0.012f * Vector2.Distance(c0, st.Spots[i]);
                float hold = st.Breaks[i] / mm.ActK - glide;
                c.Hue.Main = Color.Lerp(Color.white, mm.Hue.Main, 0.25f);
                c.Hue.Edge = Color.Lerp(Color.white, mm.Hue.Edge, 0.5f);
                bool decoy = st.Facing[i] == null;
                c.DashTo(st.Spots[i], glide).Wait(Mathf.Max(0.05f, hold)).Shatter(ShardR(level) * mm.Kk, Shard(level) * (decoy ? 0.6f : 1f));
                st.Images[i] = c;
            }, false);
            // the hall stands: the God is one more image among them, untouchable; the enemies strike the images
            float stand = 0f;
            for (int i = 0; i < mirrors; i++)
            {
                stand = Mathf.Max(stand, st.Breaks[i]);
            }
            m.Add(new Beat
            {
                Tempo = Tempo.Free,
                Dur = stand + 0.12f,
                To = new Shape(-4f, 1.04f, 0.98f, 0f, 0f),
                Ease = Ease.OutQuad,
                Forms = shows,
                Intangible = true,
                Track = false,
                Enter = mm =>
                {
                    st.T = 0f;
                    st.Veil = PsHiKit.Veil(mm, 5);
                    for (int i = 0; i < st.Facing.Count; i++)
                    {
                        if (st.Facing[i] != null)
                        {
                            Combat.Pin(st.Facing[i], stand + 0.3f);   // (each stands and stares at itself... at it)
                        }
                    }
                },
                Every = (mm, k) => Hall(mm, st)
            });
            m.WaitClones(1.5f);
            if (level >= 3)
            {
                // it was a mirror too: its own body shatters, and it stands up out of the shards
                m.Do(mm =>
                {
                    if (mm.Vis)
                    {
                        PsHiKit.Shards(mm.BodyPos(), mm.Stance, mm.Px, mm.Kk, mm.Hue, 16, 1.3f);
                        Fx.Ring(mm.BodyPos(), mm.Px, 0.25f, 2f, 16f * mm.Kk, 2, 1, FxLayer.Top, 5, 1f, 2.4f, Fx.WithA(Color.white, 0.95f), Fx.WithA(mm.Hue.Edge, 0f));
                        Kit.Flash(mm.BodyPos(), mm.Px, Mathf.RoundToInt(6f * mm.Kk), 0.1f, Fx.WithA(Color.white, 0.95f));
                    }
                    Choreo.Sound("event:/SFX/HIT/HitMetal", mm.A.current_tile);
                    mm.AreaBlow(mm.Stance, 2.2f * mm.Kk, Shard(mm.Level), null, Hurt.Reel, 0.9f, 1.1f);
                });
                m.Crouch(0.1f, 0.6f);
            }
            m.Recover(0.26f).OnEnter(mm =>
            {
                if (st.Veil != null)
                {
                    mm.Unglue(st.Veil);
                    st.Veil = null;
                }
            });
            return m.Start();
        }

        /// <summary>A frame of the hall: the sheen running over the images and the God, and each enemy striking the image before it.</summary>
        private static void Hall(ChoreoMove m, State st)
        {
            st.T += Clock.Dt;
            PsHiKit.Cover(m, st.Veil, new Color(1f, 1f, 1f, 0.22f + 0.12f * Mathf.Sin(st.T * 9f)));
            st.SheenT -= Clock.Dt;
            if (st.SheenT <= 0f && st.Images.Length > 0)
            {
                st.SheenT = 0.12f;
                Clone c = st.Images[Random.Range(0, st.Images.Length)];
                if (c != null && !c.Done)
                {
                    Sheen(m, c.Pose.Ground);
                }
            }
            for (int i = 0; i < st.Images.Length && i < st.Facing.Count; i++)
            {
                if (st.Swung[i] || st.T < st.Breaks[i] - 0.18f)
                {
                    continue;
                }
                st.Swung[i] = true;
                Actor v = st.Facing[i];
                Clone c = st.Images[i];
                if (v == null || !Combat.Living(v) || c == null || c.Done)
                {
                    continue;
                }
                Swing(m, v, c.Pose.Ground);
            }
        }

        /// <summary>A sheen running over an image: a slanted white glint line across it.</summary>
        private static void Sheen(ChoreoMove m, Vector2 feet)
        {
            if (!Kit.Visible(feet))
            {
                return;
            }
            float px = m.Px;
            Vector2 mid = feet + new Vector2(0f, m.Hpx * 0.55f * px);
            Vector2 d = new Vector2(0.55f, 1f).normalized * m.Hpx * 0.35f * px;
            Particle p = Kit.Line(mid - d, mid + d, px, 0.14f, 1, FxLayer.Over, 12, new Color(1f, 1f, 1f, 0.9f), new Color(1f, 1f, 1f, 0f));
            p?.Moving(new Vector2(m.Hpx * 0.6f * px / 0.14f, 0f));
        }

        /// <summary>The enemy strikes the image before it: a white swing from it; the image cracks (and shatters a beat later).</summary>
        private static void Swing(ChoreoMove m, Actor v, Vector2 image)
        {
            Vector2 body = Choreo.BodyAt(v, 0.55f);
            Vector2 to = image + new Vector2(0f, m.Hpx * 0.55f * m.Px);
            Choreo.Sound("event:/SFX/HIT/HitMetal", v.current_tile, 0.05f);
            if (!Kit.Visible(body))
            {
                return;
            }
            float vpx = Choreo.PxOf(v);
            bool right = to.x >= body.x;
            Particle sw = Fx.Spawn(Art.Arc(Mathf.Clamp(Mathf.RoundToInt(7f * Choreo.KOf(v)), 5, 30), 0.75f, 75f, -55f, 2), Vector2.Lerp(body, to, 0.35f), vpx, 0.12f, FxLayer.Over, 6);
            if (sw != null)
            {
                sw.SX = right ? 1f : -1f;
                sw.Color(Color.white, new Color(1f, 1f, 1f, 0.7f), new Color(1f, 1f, 1f, 0f));
            }
            // the image cracks: white lines radiating from where it was struck
            float px = m.Px;
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + Random.Range(-25f, 25f)) * Mathf.Deg2Rad;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(2.5f, 4.5f) * px * m.Kk;
                Kit.Line(to, to + d, px, 0.2f, 1, FxLayer.Top, 14, Color.white, new Color(1f, 1f, 1f, 0.8f), new Color(1f, 1f, 1f, 0f));
            }
            Fx.Glint(to, px, 0.12f, Color.white, 0.8f);
        }
    }
}
