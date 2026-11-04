using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// SKY SUNDER (Thousand Blades, Grandmaster). A deep crouch, and it is gone into the sky: a
    /// streak straight up, a twinkle at the top where it hangs upside down with the blade pointing at
    /// the earth like a star; then it falls blade-first, a comet's tail of light behind it, rights
    /// itself in the last instant and drives the blade into the ground among the foes. The earth
    /// breaks in a huge cross: four arms of light run out from the impact along the ground, tearing
    /// it open, a column of light stands up where the blade went in, and everything round it is
    /// knocked flat; those on the arms are thrown out along them. Buildings in the cross break.
    /// I: arms of 4 tiles; II: 5, wider, the crater burns; III: 6, a white-hot cross, deeper.
    /// Damage (x the user's damage): the impact I 3, II 3.8, III 4.8 (radius 2.4-3.2 tiles); the arms x0.6.
    /// Distinct from Meteor Heel and Meteor Slam (a heel, a body): a star of a blade and a cross.
    /// </summary>
    internal sealed class SkySunderTech : Technique
    {
        public override string Id => "sky_sunder";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Area;
        public override School School => School.ThousandBlades;
        public override Form Forms => Form.Blade | Form.Aerial | Form.Overhead | Form.Heavy;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 45f, 75f, 115f };
        public override float[] Cooldown => new[] { 16f, 17f, 18f };

        public override bool Accepts(Link link)
        {
            return link == Link.Scattered || link == Link.Down;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 3f : level == 2 ? 3.8f : 4.8f;
        }

        public static float Radius(int level)
        {
            return level <= 1 ? 2.4f : level == 2 ? 2.8f : 3.2f;
        }

        public static float Arm(int level)
        {
            return level <= 1 ? 4f : level == 2 ? 5f : 6f;
        }

        private static float Range(int level)
        {
            return level <= 1 ? 10f : level == 2 ? 11f : 12f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (c == null || c.Enemies.Count == 0)
            {
                return null;
            }
            float k = Choreo.KOf(c.Self);
            Plan p = Choreo.PickArea(c, this, level, Radius(level) * k, Range(level) * k, Mult(level) * 0.85f, 2);
            if (p == null && c.Focus != null)
            {
                // one foe worth a falling star: a tiered one, or a big creature
                Actor v = c.Focus;
                if (Choreo.Fair(c.Self, v, out float cm, c.Forced) && Combat.Grounded(v) && c.Dist(v) <= Range(level) * k
                    && (c.Forced || Tiers.Of(v) >= Tier.Expert || Weights.Species(v) >= Weight.Brute))
                {
                    Vector2 d = v.current_position - c.Pos;
                    p = new Plan
                    {
                        Target = v,
                        Point = v.current_position,
                        Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right,
                        Count = 1,
                        Score = c.Value(v, c.Expected(v, Mult(level)), cm),
                        Data = c.Forced
                    };
                }
            }
            if (p == null)
            {
                return null;
            }
            // it must come down on solid ground
            Vector2 at = p.Point;
            if (!Combat.SafeSpot(at) && !Combat.FindLanding(at, 1.5f, out at))
            {
                return null;
            }
            p.Point = at;
            p.AreaRadius = Radius(level) * k + Arm(level) * 0.4f;
            if (!c.Forced)
            {
                // the arms of the cross catch more of a spread-out crowd
                int onArms = 0;
                for (int i = 0; i < c.Enemies.Count && i < 16; i++)
                {
                    Vector2 rel = c.Enemies[i].current_position - at;
                    if (rel.magnitude > Radius(level) * k && (Mathf.Abs(rel.x) < 0.8f || Mathf.Abs(rel.y) < 0.8f) && rel.magnitude < Arm(level) * k)
                    {
                        onArms++;
                    }
                }
                p.Score += onArms * c.D * 0.6f;
            }
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return false;
            }
            Star m = new Star(a, this, p);
            m.Build();
            return m.Start();
        }

        /// <summary>The move: the crouch, the climb, the star at the top, the fall, the cross.</summary>
        private sealed class Star : ChoreoMove
        {
            private Vector2 _land, _apex;
            private float _apexH;
            private Hue _blade;

            public Star(Actor a, Technique t, Plan p) : base(a, t, p)
            {
                // an area blow: it comes down on a spot, not chasing one foe across the field
                SetVictim(null);
                _land = Combat.SafeSpot(p.Point) ? p.Point : (Combat.FindLanding(p.Point, 2f, out Vector2 s) ? s : Stance);
                Face(_land - Stance);
            }

            public void Build()
            {
                int level = Level;
                _blade = Armed ? Hue.As(Element.Steel) : Hue;
                _apexH = level <= 1 ? 4.5f : level == 2 ? 5.2f : 6f;
                Form air = Form.Blade | Form.Aerial | Form.Overhead | Form.Heavy;
                // the deep crouch
                Add(new Beat
                {
                    Tempo = Tempo.Wind,
                    Dur = 0.15f,
                    To = new Shape(12f, 1.22f, 0.74f, 0f, -0.03f),
                    Ease = Ease.OutQuad,
                    Track = false,
                    Forms = Form.Blade | Form.Aerial,
                    Enter = mm =>
                    {
                        if (mm.Vis)
                        {
                            Fx.LandingPuff(mm.Pose.Ground, mm.Px, mm.A.current_tile);
                            Kit.DustRing(mm.Pose.Ground, mm.Px, mm.A.current_tile, 8, -16f, 0.4f, mm.Kk);
                        }
                    }
                });
                // straight up into the sky (and part of the way over)
                Add(new Beat
                {
                    Tempo = Tempo.Travel,
                    Dur = 0.28f,
                    Route = Route.Line,
                    PathEase = Ease.OutQuad,
                    Dest = mm => ((Star)mm).ApexGround(),
                    To = new Shape(-8f, 0.84f, 1.24f, _apexH),
                    Ease = Ease.OutCubic,
                    Track = false,
                    Ghosts = 0.025f,
                    Lines = 0.03f,
                    Forms = air,
                    Enter = mm => ((Star)mm).TakeOff()
                });
                // the star at the top: it turns over, blade pointing at the earth
                Add(new Beat
                {
                    Tempo = Tempo.Free,
                    Dur = level >= 3 ? 0.26f : 0.21f,
                    To = new Shape(180f, 0.94f, 1.06f, _apexH + 0.3f),
                    Ease = Ease.InOutCubic,
                    Track = false,
                    Forms = air,
                    Enter = mm => ((Star)mm).Twinkle(),
                    Every = (mm, k) => ((Star)mm).HangBlade(k)
                });
                // the fall: blade first, a comet's tail behind; it rights itself at the last instant
                Add(new Beat
                {
                    Tempo = Tempo.Travel,
                    Dur = 0.17f,
                    Route = Route.Line,
                    PathEase = Ease.InQuad,
                    Dest = mm => ((Star)mm)._land,
                    To = new Shape(330f, 0.82f, 1.28f, 0f),
                    Ease = Ease.InQuad,
                    Track = false,
                    Forms = air,
                    Enter = mm => ((Star)mm)._apex = mm.BodyPos(),
                    Every = (mm, k) => ((Star)mm).Fall(k)
                });
                // the impact: the blade in the earth, the cross
                Add(new Beat
                {
                    Tempo = Tempo.Impact,
                    Dur = 0.15f,
                    To = new Shape(374f, 1.32f, 0.7f, 0f),
                    Ease = Ease.OutExpo,
                    Track = false,
                    Forms = air,
                    Enter = mm => ((Star)mm).Impact()
                });
                // pulls the blade out of the ground and straightens
                Add(new Beat { Tempo = Tempo.Follow, Dur = 0.14f, To = new Shape(354f, 0.94f, 1.08f, 0f), Ease = Ease.OutQuad, Track = false });
                Recover(0.34f);
            }

            private Vector2 ApexGround()
            {
                Vector2 q = Vector2.Lerp(Stance, _land, 0.45f);
                return Combat.SafeSpot(q) || Choreo.Wading(q) ? q : Stance;
            }

            private void TakeOff()
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                if (!Vis)
                {
                    return;
                }
                Kit.DustRing(Stance, Px, A.current_tile, 12, 44f, 0.5f, Kk);
                Kit.Crater(Stance, Px * Kk, A.current_tile, false, 2.5f, true);
                Kit.SpeedLines(BodyPos(), Vector2.up, Px, 5, 4f, 0.16f, Fx.WithA(Hue.Edge, 0.8f));
                Fx.Afterimage(this, 0.2f, Fx.WithA(Hue.Ghost, 0.8f), Hue.GhostEnd);
            }

            private void Twinkle()
            {
                Choreo.Sound("event:/SFX/POWERS/DivineLight", A.current_tile, 0.2f);
                if (!Vis)
                {
                    return;
                }
                Vector2 at = BodyPos();
                Fx.Glint(at, Px, 0.3f, Color.white, Level >= 3 ? 2.4f : 1.8f);
                Particle b = Fx.Spawn(Art.Burst(Mathf.RoundToInt(10f * Kk)), at, Px, 0.22f, FxLayer.Top, 20);
                if (b != null)
                {
                    b.Rot = 45f;
                    b.Sized(0.6f, 1.2f).Color(Color.white, Fx.WithA(Hue.Core, 0.8f), Fx.WithA(Hue.Edge, 0f));
                }
            }

            /// <summary>Hanging at the top, turning over: the blade swings round to point at the earth.</summary>
            private void HangBlade(float k)
            {
                Vector2 down = Vector2.Lerp(new Vector2(Fwd * 0.3f, 1f), new Vector2(0f, -1f), Beat.Apply(Ease.InOutCubic, k));
                TbHiArt.Blade(this, BodyPos(), down, Hpx * 0.75f, _blade);
                if (Vis && Random.value < 0.35f)
                {
                    Fx.Glint(BodyPos() + Random.insideUnitCircle * 3f * Px, Px, 0.12f, Hue.Core, 0.6f);
                }
            }

            /// <summary>The fall: blade first, a tail of light from the top of the sky; the body rights itself at the end.</summary>
            private void Fall(float k)
            {
                Vector2 body = BodyPos();
                Vector2 dir = (body - _apex).sqrMagnitude > 1e-4f ? (body - _apex).normalized : Vector2.down;
                TbHiArt.Blade(this, body, dir, Hpx * (0.8f + 0.4f * k), _blade);
                if (!Vis)
                {
                    return;
                }
                Kit.Line(_apex, body, Px, 0.22f, Level >= 3 ? 3 : 2, FxLayer.Top, 11, Fx.WithA(Hue.Core, 0.85f), Fx.WithA(Hue.Edge, 0.5f), Fx.WithA(Hue.Edge, 0f));
                if (Random.value < 0.6f)
                {
                    Kit.Ember(body + Random.insideUnitCircle * 2f * Px, Px, 0.3f, -dir * Random.Range(4f, 10f) * Px + Random.insideUnitCircle * 6f * Px, Hue.Edge);
                }
            }

            /// <summary>The blade goes into the earth: the column of light, the crater, the cross.</summary>
            private void Impact()
            {
                int level = Level;
                Vector2 c = Pose.Ground;
                WorldTile tile = Combat.TileAt(c);
                float r = Radius(level) * Mathf.Lerp(1f, Kk, 0.5f);
                Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionMiddle", tile);
                Choreo.Sound(Armed ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/HIT/HitStone", tile, 0.05f);
                if (Vis)
                {
                    Kit.ImpactFrame(c + new Vector2(0f, 0.4f * Kk), Px, (0.9f + 0.15f * level) * Kk, Hue.Pal, false, FxLayer.Behind);
                    Kit.Flash(c, Px, Mathf.RoundToInt(10f * Kk), 0.1f, Color.white, TopLayer);
                    // the column of light where the blade went in
                    Kit.Line(c, c + new Vector2(0f, (4f + level) * Kk), Px, 0.3f, level >= 3 ? 4 : 3, FxLayer.Top, 13, Color.white, Fx.WithA(Hue.Core, 0.8f), Fx.WithA(Hue.Edge, 0f));
                    Kit.Shockwave(c, Px, 4f, r / Px * 1.2f, 0.4f, Fx.WithA(Hue.Edge, 0.9f), 3);
                    Kit.Crater(c, Px * Kk, tile, level >= 2, 6f, false, Hue.Edge);
                    Kit.DustRing(c, Px, tile, 18, 70f, 0.6f, Kk);
                    Kit.Debris(c, Px, tile, 12, Kk);
                    Fx.FocusLines(c + new Vector2(0f, 0.5f * Kk), Px, Fx.Count(10), 16f * Kk, 30f * Kk, 0.14f);
                }
                Kit.Stop(c, 0.07f * ImpactK);
                Ruin.Quake(A, c, Scale, 1.5f, Kk);
                // the crater (its own feet kept: it never stands in a pit of its own making)
                Ruin.Blast(A, c, r * 0.65f, Scale, Pose.Ground, level >= 2);
                // everyone round the blade is knocked flat
                AreaBlow(c, r, Mult(level), null, Sakuga.Hurt.Knockdown, 1f, 1.4f);
                // the cross runs out along the ground
                Areas.Add(new Cross(this, c, Arm(level) * Mathf.Lerp(1f, Kk, 0.5f), r, Mult(level) * 0.6f, level));
                SetLink(Link.Down, null);
            }
        }

        /// <summary>
        /// The four arms of the cross racing out from the impact along the ground: each tears a furrow,
        /// glows and cools; those on an arm (beyond the crater) are thrown out along it.
        /// </summary>
        private sealed class Cross : Hazard
        {
            private static readonly Vector2[] Dirs = { Vector2.right, Vector2.up, Vector2.left, Vector2.down };
            private readonly Vector2 _at;
            private readonly float _len, _inner, _mult;
            private readonly int _level;
            private float _head, _done;

            public Cross(ChoreoMove m, Vector2 at, float len, float inner, float mult, int level) : base(m)
            {
                _at = at;
                _len = len;
                _inner = inner * 0.6f;
                _mult = mult;
                _level = level;
            }

            public override bool Tick(float dt)
            {
                if (dt <= 0f)
                {
                    return true;
                }
                Age += dt;
                float was = _head;
                _head = Mathf.Min(_len, _head + (28f + 4f * _level) * K * dt);
                const float seg = 1.1f;
                while (_done + 0.01f < _head && (_head - _done >= seg || _head >= _len))
                {
                    float b = Mathf.Min(_len, _done + seg);
                    Kit.BeginTrench();
                    try
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            Open(_at + Dirs[i] * Mathf.Max(_done, _inner * 0.5f), _at + Dirs[i] * b, Dirs[i]);
                        }
                    }
                    finally
                    {
                        Kit.EndTrench(A);
                    }
                    _done = b;
                }
                if (OwnerOk && _head > was)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        Vector2 a = _at + Dirs[i] * Mathf.Max(_inner, was - 0.3f);
                        Vector2 b = _at + Dirs[i] * _head;
                        if (_head <= _inner)
                        {
                            continue;
                        }
                        Choreo.FoesOnLine(A, a, b, 0.75f, Near, false, true);
                        for (int j = 0; j < Near.Count; j++)
                        {
                            Actor u = Near[j];
                            Vector2 on = _at + Dirs[i] * Vector2.Dot(u.current_position - _at, Dirs[i]);
                            Strike(u, _mult, Sakuga.Hurt.Launch, on - Dirs[i] * 0.4f, 1.6f + 0.3f * _level, 1.1f + 0.2f * _level, 1.2f);
                        }
                    }
                }
                return _done < _len - 0.01f;
            }

            private void Open(Vector2 a, Vector2 b, Vector2 dir)
            {
                if (OwnerOk)
                {
                    Ruin.Furrow(A, a, b, 0.7f + 0.1f * _level, Scale, A.current_position, false);
                }
                Vector2 mid = (a + b) * 0.5f;
                if (!Kit.Visible(mid, 0.3f))
                {
                    return;
                }
                WorldTile t = Combat.TileAt(mid);
                if (t == null || t.Type == null || t.Type.liquid)
                {
                    return;
                }
                Color dark = Fx.WithA(Fx.Darken(Fx.SoilColor(t), 0.6f), 0.95f);
                Particle crack = Kit.Line(a, b, Px, 3f, _level >= 3 ? 5 : 4, FxLayer.Ground, 4, dark, dark, Fx.WithA(dark, 0f));
                if (crack != null)
                {
                    crack.Snap = true;
                }
                Kit.Line(a, b, Px, 1.2f + 0.3f * _level, _level >= 3 ? 3 : 2, FxLayer.Ground, 5, Color.white, Fx.WithA(Hue.Core, 0.95f), Fx.WithA(Hue.Edge, 0.7f), Fx.WithA(Hue.Edge, 0f));
                Vector2 side = Choreo.Perp(dir);
                ChoreoFx.HeelDust(mid + side * 0.4f * K, side, Px, t, K);
                ChoreoFx.HeelDust(mid - side * 0.4f * K, -side, Px, t, K);
                Kit.Debris(b, Px, t, 2, K);
            }
        }
    }
}
