using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// HEAVEN'S FLICK (Hunter's Way, God). What a God does about a monster that is no threat to it: a
    /// dragon, a bear, a demon, a swarm of rats. It barely looks: a hand comes up lazily, a finger
    /// cocked against the thumb (a glint), and it flicks. The air itself hits: a white line of
    /// pressure from the fingertip, a sharp ring bursting on the monster, and the monster is gone,
    /// sent tumbling off over the horizon (a dragon included: whatever the game lets anything move
    /// is moved, however heavy). A body the flick would have killed dies where it comes down. The
    /// God is already looking elsewhere. It reaches from a few tiles away (the pressure of the
    /// flick); further, the God strolls in first. Crabzilla and the like cannot be flicked away and
    /// are left to the Kaiju Grapple. I: 7 tiles away; II: 11; III: 16, off the screen. Damage (x
    /// the user's damage): I 2.0, II 2.8, III 3.6. A Casual technique: it is what the Brain picks
    /// against things far weaker than a God (Threat.Fit), and costs next to nothing.
    /// </summary>
    internal sealed class HeavensFlickTech : Technique
    {
        public override string Id => "heavens_flick";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Single;
        public override School School => School.HuntersWay;
        public override Form Forms => Form.Hunt | Form.Fast;
        public override Prey Prey => BigGame.Monsters;
        public override Scale Scale => Scale.Casual;
        public override float[] ManaCost => new[] { 12f, 16f, 20f };
        public override float[] Cooldown => new[] { 8f, 8f, 8f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 2f : level == 2 ? 2.8f : 3.6f;
        }

        /// <summary>How far the flicked one flies (tiles).</summary>
        public static float Away(int level)
        {
            return level <= 1 ? 7f : level == 2 ? 11f : 16f;
        }

        /// <summary>How far the flick's pressure reaches without a step (tiles).</summary>
        public static float Reach(int level)
        {
            return level <= 1 ? 3f : level == 2 ? 3.6f : 4.2f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            MeleeWant w = new MeleeWant
            {
                Reach = Reach(level) + 4f,
                Total = Mult(level),
                Worthy = false,
                Crowd = 0f,
                Alone = 4f,
                IntoGuard = 1f,
                Worth = 12f
            };
            Plan p = BigGame.Pick(c, level, w, Prey.None, true, 0f, true);
            if (p == null)
            {
                return null;
            }
            Actor v = p.Target;
            if (!c.Forced)
            {
                // only what a flick can send away (Crabzilla is the grapple's)
                if (!Flickable(v, c.Self))
                {
                    return null;
                }
                // a monster out of the fight is worth more than its health: the bigger, the more
                p.Score += 18f + 10f * Mathf.Min(3f, c.Threat(v) * 5f);
            }
            return p;
        }

        /// <summary>Can a flick send it flying (puppeted by the engine, or thrown by the game's forces)?</summary>
        public static bool Flickable(Actor v, Actor by)
        {
            if (v == null || v.asset == null || ControllableUnit.isControllingUnit(v) || v.is_immovable || v.asset.is_boat)
            {
                return false;
            }
            return Victims.Movable(v, by) || v.asset.can_be_moved_by_powers;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null || m.V == null)
            {
                return false;
            }
            Flick flick = new Flick(m);
            int level = m.Level;
            // further than the flick's pressure reaches: a stroll in, unhurried
            m.Add(new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.06f,
                PerTile = 0.05f,
                Route = Route.Line,
                PathEase = Ease.InOutSine,
                Dest = mm => flick.Near(mm),
                To = new Shape(4f, 1f, 1f, 0f),
                Ease = Ease.OutQuad,
                Ghosts = 0.06f,
                If = mm => flick.Far(mm),
                Forms = Form.Hunt | Form.Dash
            });
            // the hand comes up, the finger cocked against the thumb: a glint
            m.PoseTo(level <= 1 ? 0.08f : 0.06f, new Shape(-3f, 0.98f, 1.02f, 0f, -0.02f), Ease.OutQuad, Tempo.Wind).OnEnter(mm => flick.Cock(mm));
            m.Hold(level <= 1 ? 0.05f : 0.03f, false, Tempo.Wind);
            // the flick
            Beat act = new Beat { Tempo = Tempo.Act, Dur = 0.05f, To = new Shape(5f, 1.03f, 0.98f, 0f, 0.03f), Ease = Ease.OutExpo, Forms = Forms };
            act.On(0.3f, mm => flick.Release(mm));
            m.Add(act);
            // and it has already looked away
            m.PoseTo(0.14f, new Shape(-2f, 1f, 1f, 0f, 0f), Ease.OutQuad, Tempo.Follow).OnEnter(mm => flick.LookAway(mm));
            m.Recover(0.12f);
            return m.Start();
        }
    }

    /// <summary>One Heaven's Flick: the cocked finger, the pressure line, the monster sent over the horizon.</summary>
    internal sealed class Flick
    {
        private readonly ChoreoMove _m;

        public Flick(ChoreoMove m)
        {
            _m = m;
        }

        private float Reach(ChoreoMove m)
        {
            return HeavensFlickTech.Reach(m.Level) * Mathf.Lerp(1f, m.Kk, 0.5f) + (m.V != null ? m.V.stats["size"] * 0.5f : 0f);
        }

        public bool Far(ChoreoMove m)
        {
            return m.V != null && Vector2.Distance(m.Stance, m.VG) > Reach(m);
        }

        /// <summary>Close enough for the flick's pressure, on its own side.</summary>
        public Vector2 Near(ChoreoMove m)
        {
            if (m.V == null)
            {
                return m.Stance;
            }
            Vector2 d = m.Stance - m.VG;
            Vector2 dir = d.sqrMagnitude > 1e-4f ? d.normalized : -m.Dir;
            Vector2 want = Combat.Clamp(m.VG + dir * Reach(m) * 0.8f);
            if (Combat.SafeSpot(want) && Combat.ClearPath(m.Stance, want))
            {
                return want;
            }
            return m.Stance;
        }

        private Vector2 Finger(ChoreoMove m)
        {
            return m.BodyPos() + new Vector2(m.Fwd * m.Hpx * 0.32f, m.Hpx * 0.12f) * m.Px;
        }

        private Vector2 Mark(Actor v)
        {
            if (BigGame.IsDragon(v) || v.asset.has_avatar_prefab)
            {
                Wingbreaker.Box b = BigGame.Size(v);
                return v.current_position + new Vector2(b.HeadX * 0.5f, b.Center);
            }
            return Choreo.BodyAt(v, 0.55f);
        }

        public void Cock(ChoreoMove m)
        {
            if (m.Vis && m.Level >= 2)
            {
                Fx.Glint(Finger(m), m.Px, 0.12f, m.Hue.Core, 0.7f);
            }
        }

        /// <summary>The flick: the pressure hits, the monster goes.</summary>
        public void Release(ChoreoMove m)
        {
            Actor v = m.V;
            if (!m.VOk || v == null)
            {
                m.Lose();
                return;
            }
            Vector2 finger = Finger(m);
            Vector2 at = Mark(v);
            Choreo.Sound("event:/SFX/HIT/HitMetal", m.A.current_tile, 0.05f);
            if (m.Vis)
            {
                Kit.Flash(finger, m.Px, 2, 0.04f, Fx.WithA(Color.white, 0.95f), m.TopLayer);
                Kit.Line(finger, at, m.Px, 0.07f, 1, m.TopLayer, 15, Color.white, Fx.WithA(m.Hue.Core, 0.7f), Fx.WithA(m.Hue.Edge, 0f));
            }
            if (!m.Touch(v))
            {
                return;   // (dodged or turned: the pressure passes through an afterimage)
            }
            int level = m.Level;
            Combat.Allowed(m.A, v, out float cm);
            HitResult r = Combat.Hit(m.A, v, HeavensFlickTech.Mult(level) * cm, false, false, 0f, m);
            m.LastHit = r;
            bool owed = MeteorSlam.AtDeathsDoor(v);
            Vector2 dir = v.current_position - m.Stance;
            if (dir.sqrMagnitude < 1e-4f)
            {
                dir = m.Dir;
            }
            dir.Normalize();
            // a little upward: over the horizon
            float away = HeavensFlickTech.Away(level);
            float apex = 1.6f + 0.9f * level;
            bool gone;
            if (Victims.Movable(v, m.A))
            {
                Impact im = Impact.Of(Hurt.Launch, dir, away, 2f);
                im.Lift = apex;
                im.Owed = owed;
                im.Spin = (Random.value < 0.5f ? -1f : 1f) * 1080f;
                Victims.Hurt(v, m.A, m, im);
                gone = true;
            }
            else
            {
                // (the game's forces carry a giant a good deal less far than they promise: it flies further)
                gone = BigGame.Hurl(v, dir, away * 2.4f, apex * 1.4f);
                if (gone)
                {
                    Trails.Follow(v, 1.2f, Fx.WithA(m.Hue.Ghost, 0.5f));
                    if (owed)
                    {
                        MeteorSlam.Doom(m.A, v, m);
                    }
                }
                else
                {
                    Victims.Hurt(v, m.A, m, Impact.Of(Hurt.Stagger, dir, 0f, 2f));
                    if (owed)
                    {
                        MeteorSlam.Doom(m.A, v, m);
                    }
                }
            }
            Victims.Flash(v, Color.white, 0.07f);
            WorldTile tile = v.current_tile;
            Choreo.Sound("event:/SFX/HIT/HitGeneric", tile);
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", tile, 0.05f);
            if (Kit.Visible(at, 0.4f))
            {
                float vk = Mathf.Clamp(Choreo.KOf(v), 1f, 2.6f);
                ChoreoFx.Impact(at, dir, m.Px, m.Kk * vk, 1.6f, m.Hue, m.TopLayer, true);
                Fx.Ring(at, m.Px, 0.22f, 2f, (10f + 3f * level) * vk, 2, 1, FxLayer.Top, 8, 1f, 2.6f, Color.white, Fx.WithA(m.Hue.Core, 0.8f), Fx.WithA(m.Hue.Edge, 0f));
                Kit.SpeedLines(at, dir, m.Px, 3 + level, 5f * vk, 0.16f, Fx.WithA(Color.white, 0.85f));
                if (gone)
                {
                    // where it went: a twinkle off on the horizon as it disappears
                    Particle tw = Fx.Glint(at + dir * 4f * m.Px + new Vector2(0f, 3f * m.Px), m.Px, 0.35f, Color.white, 1.2f);
                    if (tw != null)
                    {
                        tw.Delay = 0.3f + 0.05f * level;
                        tw.Pos = at + dir * (2f + level) * 0.6f + new Vector2(0f, 1.5f + 0.5f * level);
                    }
                }
            }
            // a God's casual blow still shakes the ground a little at III
            if (level >= 3)
            {
                Ruin.Quake(m.A, at, m.Scale, 0.8f, m.Kk);
            }
            m.SetLink(Link.None, null);
            if (Dev.On) Dev.Trace("heavens_flick " + v.asset.id + "#" + v.Sid() + " x" + (HeavensFlickTech.Mult(level) * cm).ToString("F2") + (gone ? " flicked away " + away + " tiles" : " (too heavy: rocked)") + (owed ? " (owed)" : ""));
        }

        /// <summary>It has already looked away (to the next foe, or simply away).</summary>
        public void LookAway(ChoreoMove m)
        {
            Actor v = m.V;
            m.SetVictim(null);
            if (v != null)
            {
                Vector2 d = m.Stance - v.current_position;
                if (Mathf.Abs(d.x) > 0.05f && Random.value < 0.5f)
                {
                    m.Face(new Vector2(Mathf.Sign(d.x), 0f));
                }
            }
        }
    }
}
