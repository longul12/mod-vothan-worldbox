using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    internal partial class ChoreoMove
    {
        // ------------------------------------------------------------- places --

        /// <summary>Destination: a blow's length in front of the target.</summary>
        public static Vector2 ToTarget(ChoreoMove m)
        {
            return m.V != null ? m.StandSpot(m.Stand) : m.Stance;
        }

        /// <summary>Destination: right behind the target (its back), or in front if that side is blocked.</summary>
        public static Vector2 BehindTarget(ChoreoMove m)
        {
            if (m.V == null)
            {
                return m.Stance;
            }
            Vector2 d = m.VG - m.Stance;
            Vector2 dir = d.sqrMagnitude > 1e-4f ? d.normalized : m.Dir;
            Vector2 s = Combat.Clamp(m.VG + dir * m.Stand);
            return Combat.SafeSpot(s) ? s : m.StandSpot(m.Stand);
        }

        /// <summary>Destination: the plan's point.</summary>
        public static Vector2 ToPoint(ChoreoMove m)
        {
            Vector2 p = Combat.Clamp(m.Point);
            return Combat.SafeSpot(p) || Combat.FindLanding(p, 2f, out p) ? p : m.Stance;
        }

        /// <summary>Destination: where it stands now (a leap straight up).</summary>
        public static Vector2 Here(ChoreoMove m)
        {
            return m.Stance;
        }

        /// <summary>'d' tiles along its facing direction from where it stands (safe ground, or as far as there is).</summary>
        public Vector2 Ahead(float d)
        {
            Vector2 dir = Dir;
            for (int i = 0; i < 6; i++)
            {
                Vector2 q = Combat.Clamp(Stance + dir * d);
                if (Combat.SafeSpot(q) && Combat.ClearPath(Stance, q))
                {
                    return q;
                }
                d *= 0.6f;
            }
            return Stance;
        }

        // --------------------------------------------------------- movement --

        /// <summary>
        /// A quick step into striking range of the target (leaning into it, a puff from the heel).
        /// Nothing if it is already there.
        /// </summary>
        public ChoreoMove StepIn()
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.06f,
                PerTile = 0.04f,
                Route = Route.Line,
                PathEase = Ease.OutQuad,
                Dest = ToTarget,
                Ease = Ease.Linear,
                NeedsVictim = true,
                Ghosts = 0.05f,
                Dust = 0.085f,
                If = m => m.V != null && Vector2.Distance(m.Stance, m.StandSpot(m.Stand)) > 0.18f * m.Kk
            };
            b.Enter = m =>
            {
                if (m.Vis)
                {
                    Fx.LandingPuff(m.Stance, m.Px, m.A.current_tile);
                }
            };
            b.Every = (m, k) =>
            {
                float lean = Mathf.Sin(Mathf.PI * k);
                m.Pose.Rot = Mathf.Lerp(m._s0.Rot, 0f, k) + 14f * lean;
                m.Pose.SX = 1f + 0.12f * lean;
                m.Pose.SY = 1f - 0.1f * lean;
            };
            return Add(b);
        }

        /// <summary>
        /// A run-up to 'to' (default: in front of the target): no blur, real strides: the body bobs with
        /// each step, leaning into the run, a puff of dust at every footfall. 'perTile': seconds per
        /// tile at Novice pace (a run, not a dash).
        /// </summary>
        public ChoreoMove RunUp(Func<ChoreoMove, Vector2> to = null, float perTile = 0.09f)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.05f,
                PerTile = perTile,
                Route = Route.Line,
                PathEase = Ease.InOutSine,
                Dest = to ?? ToTarget,
                Ease = Ease.Linear,
                Forms = Form.Dash
            };
            float stepT = 0f;
            b.Enter = m =>
            {
                stepT = 0f;
                if ((m._dest - m.Stance).sqrMagnitude > 1e-4f)
                {
                    m.Face(m._dest - m.Stance);
                }
            };
            b.Every = (m, k) =>
            {
                // strides: a bob every 0.11 s of the run, a puff at each footfall
                float stride = 0.11f * m.ActK;
                float ph = m.BeatT / stride;
                float bob = Mathf.Abs(Mathf.Sin(Mathf.PI * ph));
                float lean = Mathf.Sin(Mathf.PI * Mathf.Min(1f, k * 1.1f));
                m.Pose.Height = 0.05f * m.Kk * bob * lean;
                m.Pose.Rot = Mathf.Lerp(m._s0.Rot, 0f, Mathf.Clamp01(k * 3f)) + 12f * lean;
                m.Pose.SX = 1f + 0.05f * (1f - bob);
                m.Pose.SY = 1f - 0.05f * (1f - bob);
                stepT -= Clock.Dt;
                if (stepT <= 0f && k < 0.95f)
                {
                    stepT = stride;
                    if (m.Vis)
                    {
                        ChoreoFx.HeelDust(m.Pose.Ground, m._g0 - m._dest, m.Px, m.A.current_tile, m.Kk * 0.7f);
                    }
                }
            };
            return Add(b);
        }

        /// <summary>
        /// A blur dash to 'to' (default: in front of the target): stretched along the run, afterimages,
        /// speed lines, dust; nothing can touch it on the way. 'perTile': seconds per tile at Novice pace.
        /// </summary>
        public ChoreoMove Dash(Func<ChoreoMove, Vector2> to = null, float perTile = 0.028f)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.05f,
                PerTile = perTile,
                Route = Route.Line,
                PathEase = Ease.OutQuad,
                Dest = to ?? ToTarget,
                Ease = Ease.Linear,
                Intangible = true,
                Ghosts = 0.022f,
                Lines = 0.03f,
                Forms = Form.Dash
            };
            b.Enter = m =>
            {
                if ((m._dest - m.Stance).sqrMagnitude > 1e-4f)
                {
                    m.Face(m._dest - m.Stance);
                }
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                if (m.Vis)
                {
                    Fx.LandingPuff(m.Stance, m.Px, m.A.current_tile);
                    ChoreoFx.HeelDust(m.Stance, m.Stance - m._dest, m.Px, m.A.current_tile, m.Kk);
                }
            };
            b.Every = (m, k) =>
            {
                float lean = Mathf.Sin(Mathf.PI * Mathf.Min(1f, k * 1.15f));
                m.Pose.Rot = Mathf.Lerp(m._s0.Rot, 0f, Mathf.Clamp01(k * 3f)) + 16f * lean;
                m.Pose.SX = 1f + 0.2f * (1f - k);
                m.Pose.SY = 1f - 0.13f * (1f - k);
            };
            b.Exit = m =>
            {
                if (m.Vis)
                {
                    Fx.LandingPuff(m.Pose.Ground + m.Dir * m.Px, m.Px, m.A.current_tile);
                }
            };
            return Add(b);
        }

        /// <summary>
        /// A blink: a pop where it stood (a flash, an afterimage left behind), gone (nothing can touch it),
        /// and it is at 'to' (default: behind the target) in a ring of its colour. 'dur': the gap.
        /// </summary>
        public ChoreoMove Blink(Func<ChoreoMove, Vector2> to = null, float dur = 0.08f)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = dur,
                Route = Route.Blink,
                Dest = to ?? BehindTarget,
                Intangible = true,
                Forms = Form.Teleport
            };
            b.Enter = m =>
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                if (m.Vis)
                {
                    Particle flash = Fx.Afterimage(m, 0.07f, new Color(1f, 1f, 1f, 0.95f), new Color(1f, 1f, 1f, 0f));
                    if (flash != null)
                    {
                        flash.R.sortingOrder += 3;
                    }
                    Fx.Afterimage(m, 0.26f, Fx.WithA(m.Hue.Ghost, 0.75f), m.Hue.GhostEnd);
                    Fx.Ring(m.Stance, m.Px, 0.16f, 6f * m.Kk, 1f, 1, 1, FxLayer.Ground, 4, Art.Ground, 1.6f, Fx.WithA(m.Hue.Edge, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
                    Vector2 d = m._dest - m.Stance;
                    if (d.sqrMagnitude > 0.01f)
                    {
                        Kit.SpeedLines(Vector2.Lerp(m.Stance, m._dest, 0.5f) + new Vector2(0f, m.Pose.PivotUp * m.A.actor_scale), d, m.Px, 4, 4f, 0.12f, Fx.WithA(m.Hue.Edge, 0.85f));
                    }
                }
            };
            b.Exit = m =>
            {
                if ((m.VG - m.Stance).sqrMagnitude > 1e-4f && m.V != null)
                {
                    m.Face(m.VG - m.Stance);
                }
                if (m.Vis)
                {
                    Fx.Ring(m.Stance, m.Px, 0.2f, 2f, 9f * m.Kk, 1, 1, FxLayer.Ground, 4, Art.Ground, 2.5f, Fx.WithA(m.Hue.Edge, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
                    Fx.Afterimage(m, 0.12f, Fx.WithA(m.Hue.Core, 0.8f), Fx.WithA(m.Hue.Edge, 0.5f), m.Hue.GhostEnd);
                }
            };
            return Add(b);
        }

        /// <summary>A crouch (anticipation of a leap, a charge, a heavy blow): squashed, leaning in, a puff.</summary>
        public ChoreoMove Crouch(float dur = 0.12f, float depth = 1f)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Wind,
                Dur = dur,
                To = new Shape(10f * depth, 1f + 0.16f * depth, 1f - 0.2f * depth, 0f),
                Ease = Ease.OutQuad
            };
            b.Enter = m =>
            {
                if (m.Vis)
                {
                    Fx.LandingPuff(m.Pose.Ground, m.Px, m.A.current_tile);
                }
            };
            return Add(b);
        }

        /// <summary>
        /// A leap along an arc to 'to' (default: where it stands), 'apex' tiles high (x size), 'dur'
        /// seconds, turning 'spin' degrees on the way (360: a forward flip). Takeoff: a stretched frame
        /// and a ring of dust. It comes down at 'land' tiles high (0: on the ground; follow it with Land).
        /// </summary>
        public ChoreoMove Leap(Func<ChoreoMove, Vector2> to = null, float apex = 1f, float dur = 0.4f, float spin = 0f, float land = 0f)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = dur,
                Route = Route.Arc,
                PathEase = Ease.InOutSine,
                Dest = to ?? Here,
                Apex = apex,
                Spin = spin,
                To = new Shape(float.NaN, 1f, 1f, land),
                Ease = Ease.OutQuad,
                Ghosts = 0.035f,
                Forms = Form.Aerial
            };
            b.Enter = m =>
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                if (m.Vis)
                {
                    Kit.DustRing(m.Stance, m.Px, m.A.current_tile, 8, 36f, 0.45f, m.Kk);
                    Kit.SpeedLines(m.BodyPos(), Vector2.up, m.Px, 3, 3f, 0.14f, Fx.WithA(m.Hue.Edge, 0.7f));
                }
            };
            b.Every = (m, k) =>
            {
                // stretched as it leaves the ground and as it comes down, round at the top
                float fast = Mathf.Abs(1f - 2f * k);
                if (k < 0.12f)
                {
                    fast = 1f;
                }
                m.Pose.SX = 1f - 0.12f * fast;
                m.Pose.SY = 1f + 0.18f * fast;
                m.Pose.HideShadow = false;
            };
            return Add(b);
        }

        /// <summary>
        /// The landing: squashed on the ground, a puff, and (crater > 0) the ground breaks under it at the
        /// move's Scale (Ruin), a shockwave ring; 'area' > 0: its enemies within that many tiles are hit
        /// (x 'mult') and knocked down. 'fire': the crater may set the grass alight (false: it only breaks
        /// the ground, an Earth Breaker's landing).
        /// </summary>
        public ChoreoMove Land(float crater = 0f, float area = 0f, float mult = 0.5f, bool fire = true)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Follow,
                Dur = 0.1f,
                To = new Shape(4f, 1.22f, 0.78f, 0f),
                Ease = Ease.OutExpo,
                Track = false
            };
            b.Enter = m =>
            {
                m.Pose.Height = 0f;
                WorldTile tile = m.A.current_tile;
                Choreo.Sound(crater > 0f ? "event:/SFX/DESTRUCTION/DropSimpleImpact" : "event:/SFX/HIT/HitGeneric", tile);
                if (m.Vis)
                {
                    Fx.LandingPuff(m.Pose.Ground, m.Px, tile);
                    Kit.DustRing(m.Pose.Ground, m.Px, tile, crater > 0f ? 12 : 6, crater > 0f ? 44f : 28f, 0.5f, m.Kk);
                }
                if (crater > 0f)
                {
                    if (m.Vis)
                    {
                        Kit.Crater(m.Pose.Ground, m.Px * m.Kk, tile, Ruin.Force(m.Tier, m.Scale) >= 3, 6f, crater < 1.2f, m.Hue.Edge);
                        Kit.Shockwave(m.Pose.Ground, m.Px, 4f, Mathf.Max(12f, Ruin.Radius(m.Tier, m.Scale, crater) / m.Px), 0.35f, Fx.WithA(m.Hue.Edge, 0.9f), 2);
                        Kit.Debris(m.Pose.Ground, m.Px, tile, 8, m.Kk);
                    }
                    Ruin.Blast(m.A, m.Pose.Ground, crater, m.Scale, m.Pose.Ground, fire);
                    Ruin.Quake(m.A, m.Pose.Ground, m.Scale, 1f, m.Kk);
                }
                if (area > 0f)
                {
                    m.AreaBlow(m.Pose.Ground, area, mult, null, Hurt.Knockdown, 0.6f);
                }
            };
            return Add(b);
        }

        /// <summary>A quick side-step (a slip): 'side' tiles to its left (negative: right), leaning away, an afterimage left where it was.</summary>
        public ChoreoMove Slip(float side = 0.5f, float dur = 0.1f)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = dur,
                Route = Route.Line,
                PathEase = Ease.OutCubic,
                Dest = m =>
                {
                    Vector2 q = Combat.Clamp(m.Stance + m.Side * side * m.Kk);
                    return Combat.SafeSpot(q) && Combat.ClearPath(m.Stance, q) ? q : m.Stance;
                },
                To = new Shape(-10f, 0.9f, 1.04f, 0f),
                Ease = Ease.OutQuad,
                Forms = Form.Evasive
            };
            b.Enter = m =>
            {
                if (m.Vis)
                {
                    Fx.Afterimage(m, 0.2f, Fx.WithA(m.Hue.Ghost, 0.7f), m.Hue.GhostEnd);
                    ChoreoFx.HeelDust(m.Stance, m.Stance - m._dest, m.Px, m.A.current_tile, m.Kk);
                }
            };
            return Add(b);
        }

        /// <summary>A hop back out of reach, facing the target ('dist' tiles), landing light.</summary>
        public ChoreoMove BackHop(float dist = 0.8f, float dur = 0.2f)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = dur,
                Route = Route.Arc,
                PathEase = Ease.OutQuad,
                Apex = 0.22f,
                Dest = m =>
                {
                    Vector2 q = Combat.Clamp(m.Stance - m.Dir * dist * m.Kk);
                    return Combat.SafeSpot(q) && Combat.ClearPath(m.Stance, q) ? q : m.Stance;
                },
                To = new Shape(-12f, 0.94f, 1.08f, 0f),
                Ease = Ease.OutQuad,
                Ghosts = 0.04f,
                Forms = Form.Evasive
            };
            b.Enter = m =>
            {
                if (m.Vis)
                {
                    ChoreoFx.HeelDust(m.Stance, m.Dir, m.Px, m.A.current_tile, m.Kk);
                }
            };
            b.Exit = m =>
            {
                if (m.Vis)
                {
                    Fx.LandingPuff(m.Pose.Ground, m.Px, m.A.current_tile);
                }
            };
            return Add(b);
        }

        /// <summary>Runs round the target (or the plan's point): 'turn' radians (PI: to its far side), at 'r' tiles (-1: where it is).</summary>
        public ChoreoMove Circle(float turn = Mathf.PI, float r = -1f, float dur = 0.22f)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = dur,
                Route = Route.Orbit,
                Dest = m =>
                {
                    Vector2 c = m.V != null ? m.VG : m.Point;
                    Vector2 rel = m.Stance - c;
                    float rr = r > 0f ? r * m.Kk : Mathf.Max(m.Stand, rel.magnitude);
                    float th = Mathf.Atan2(rel.y / Art.Ground, rel.x) + turn;
                    Vector2 q = Combat.Clamp(c + new Vector2(Mathf.Cos(th), Mathf.Sin(th) * Art.Ground) * rr);
                    return Combat.SafeSpot(q) ? q : m.Stance;
                },
                To = new Shape(12f, 1.12f, 0.92f, 0f),
                Ease = Ease.OutQuad,
                Ghosts = 0.02f,
                Dust = 0.05f,
                Forms = Form.Dash
            };
            b.Enter = m =>
            {
                m.OrbitCenter = m.V != null ? m.VG : m.Point;
                m.OrbitTurn = turn;
                m.OrbitR = r > 0f ? r * m.Kk : -1f;
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
            };
            return Add(b);
        }

        /// <summary>
        /// Steps up the air: 'steps' hops climbing to 'height' tiles (x size), each step on a little ring of
        /// wind under its feet, towards 'to' (default: above the target).
        /// </summary>
        public ChoreoMove AirWalk(float height, int steps = 3, float stepDur = 0.12f, Func<ChoreoMove, Vector2> to = null)
        {
            steps = Mathf.Clamp(steps, 1, 8);
            for (int i = 1; i <= steps; i++)
            {
                float hi = height * i / steps;
                float frac = i / (float)steps;
                Beat b = new Beat
                {
                    Tempo = Tempo.Travel,
                    Dur = stepDur,
                    Route = Route.Arc,
                    PathEase = Ease.OutQuad,
                    Apex = 0.12f,
                    Dest = m =>
                    {
                        Vector2 goal = to != null ? to(m) : (m.V != null ? m.VG - m.Dir * m.Stand * 0.5f : m.Stance);
                        return Vector2.Lerp(m.Stance, goal, 1f / Mathf.Max(1, steps - Mathf.RoundToInt(frac * steps) + 1));
                    },
                    To = new Shape(-6f, 0.92f, 1.1f, hi),
                    Ease = Ease.OutQuad,
                    Ghosts = 0.05f,
                    Forms = Form.Aerial
                };
                b.Enter = m =>
                {
                    if (m.Vis)
                    {
                        Vector2 feet = m.Pose.Ground + new Vector2(0f, m.Pose.Height);
                        Fx.Ring(feet, m.Px, 0.22f, 2f, 7f * m.Kk, 1, 1, FxLayer.Top, 4, Art.Ground, 2.2f, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
                    }
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile, 0.06f);
                };
                Add(b);
            }
            return this;
        }

        /// <summary>Spins in place 'turns' times over 'dur' (on one leg: stretched a little), dust swirling at its feet.</summary>
        public ChoreoMove SpinInPlace(float turns = 1f, float dur = 0.3f)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Act,
                Dur = dur,
                To = new Shape(0f, 1f, 1.06f, 0f),
                Ease = Ease.OutQuad,
                Track = false,
                Forms = Form.Spin
            };
            float baseFace = 0f;
            b.Enter = m =>
            {
                baseFace = m.Fwd;
                if (m.Vis)
                {
                    Kit.DustRing(m.Pose.Ground, m.Px, m.A.current_tile, 8, 24f, 0.4f, m.Kk);
                }
            };
            b.Every = (m, k) =>
            {
                float t = Beat.Apply(Ease.InOutSine, k) * turns;
                float c = Mathf.Cos(Mathf.PI * 2f * t);
                m.Pose.SX *= Mathf.Max(0.2f, Mathf.Abs(c));
                float frac = t - Mathf.Floor(t);
                bool back = frac > 0.25f && frac < 0.75f;
                m.Pose.FaceRight = back ? baseFace < 0f : baseFace > 0f;
            };
            b.Exit = m => m.Pose.FaceRight = m.Fwd > 0f;
            return Add(b);
        }

        /// <summary>
        /// A guard: crouched behind its forearms for 'dur' seconds (showing Form.Guard to readers), a shield
        /// of its colours glowing in front of it ('hex': a hexagon barrier instead of a forearm guard), a
        /// pixel's tremble when it is struck. Pair it with your own Hooks.BeforeDamage handler for what
        /// the guard does to blows.
        /// </summary>
        public ChoreoMove GuardUp(float dur, bool hex = false, Element e = Element.School)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Free,
                Dur = dur,
                To = new Shape(-6f, 1.08f, 0.9f, 0f, -0.03f),
                Ease = Ease.OutQuad,
                Forms = Form.Guard
            };
            Particle shield = null;
            b.Enter = m =>
            {
                Hue h = e == Element.School ? m.Hue : m.Hue.As(e);
                Sprite s = hex ? ChoreoArt.Hex(Mathf.RoundToInt(m.Hpx * 0.7f), 1) : ChoreoArt.Crescent(Mathf.RoundToInt(m.Hpx * 0.55f), 2);
                shield = m.Glue(Fx.Spawn(s, m.BodyPos(), m.Px, 1f, FxLayer.Over, 9));
                if (shield != null)
                {
                    shield.Color(Fx.WithA(h.Core, 0.85f));
                }
                Choreo.Sound("event:/SFX/HIT/HitMetal", m.A.current_tile, 0.2f);
            };
            b.Every = (m, k) =>
            {
                if (shield == null || !shield.Alive)
                {
                    return;
                }
                Hue h = e == Element.School ? m.Hue : m.Hue.As(e);
                shield.Pos = m.BodyPos() + new Vector2(m.Fwd * m.Hpx * (hex ? 0.45f : 0.3f), 0f) * m.Px;
                shield.SX = hex ? 1f : m.Fwd;
                shield.Grad = null;
                float pulse = 0.65f + 0.25f * Mathf.Sin(m.BeatT * 12f);
                shield.R.color = Fx.WithA(Color.Lerp(h.Core, h.Edge, 0.4f), pulse * Mathf.Clamp01(Mathf.Min(k * 8f, (1f - k) * 8f)));
            };
            b.Exit = m =>
            {
                if (shield != null)
                {
                    m.Unglue(shield);
                    shield = null;
                }
            };
            return Add(b);
        }

        /// <summary>Rises straight up to 'height' tiles (x size) over 'dur' (a hover, a jump to meet a juggled body).</summary>
        public ChoreoMove Rise(float height, float dur = 0.2f, Ease e = Ease.OutCubic)
        {
            return Add(new Beat { Tempo = Tempo.Travel, Dur = dur, To = new Shape(-4f, 0.9f, 1.14f, height), Ease = e, Ghosts = 0.04f, Forms = Form.Aerial });
        }

        /// <summary>Drops from the air to 'height' (0: the ground) over 'dur', accelerating (follow it with Land).</summary>
        public ChoreoMove Drop(float dur = 0.16f, float height = 0f, float lean = 10f)
        {
            return Add(new Beat { Tempo = Tempo.Travel, Dur = dur, To = new Shape(lean, 0.86f, 1.18f, height), Ease = Ease.InQuad, Ghosts = 0.03f, Forms = Form.Aerial | Form.Overhead });
        }

        /// <summary>
        /// Runs up a giant's body to its head (titan-topplers): a dash to its feet, then up its height
        /// (x 'frac'), leaning into the climb, dust kicked off its hide.
        /// </summary>
        public ChoreoMove Climb(float frac = 0.85f, float dur = 0.3f)
        {
            Dash();
            Beat b = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = dur,
                Route = Route.Line,
                PathEase = Ease.OutQuad,
                Dest = m => m.V != null ? Vector2.Lerp(m.Stance, m.VG, 0.5f) : m.Stance,
                To = new Shape(28f, 0.9f, 1.12f, 0f),
                Ease = Ease.OutQuad,
                Ghosts = 0.03f,
                NeedsVictim = true,
                Forms = Form.Aerial
            };
            b.Every = (m, k) =>
            {
                m.Pose.Height = Mathf.Max(m.Pose.Height, m.VHeight * frac * Beat.Apply(Ease.OutQuad, k));
                if (m.Vis && Random.value < 0.3f)
                {
                    ChoreoFx.HeelDust(m.Pose.Ground + new Vector2(0f, m.Pose.Height), Vector2.down, m.Px, m.A.current_tile, m.Kk);
                }
            };
            return Add(b);
        }
    }
}
