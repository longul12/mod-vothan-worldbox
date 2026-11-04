using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// FINAL RADIANCE (Ki Arts, God). The beam of beams. The world dims; the God rises off the ground
    /// in a column of its own light, the earth cracking under it and the rocks lifting into the air
    /// round it, and every mote of light on the field streams into its body until it shines like a
    /// second sun (Charging: the longest gather of all; a fast enough blow breaks it). Then it throws
    /// both palms forward and the light leaves it all at once: an impact frame, the world slowed, a
    /// beam wider than a house, white to its core inside a halo of the God's own colour, rings
    /// spiralling down its length, ploughing a canyon through the land; everyone in it is driven
    /// along it and blasted off its end, and where it ends the earth bursts. It wins every beam
    /// clash, save against another Final Radiance (BeamSpec.AlwaysWins); fired back at a beam
    /// already coming, it is drawn in a heartbeat. Ultimate: a cinematic (veil, title, slow motion
    /// at the release) when the settings allow; it plays without one otherwise.
    /// I (a bare trait): 28 tiles, 14 px wide. II: 32, 16 px. III (Gods): 36, 18 px.
    /// Damage to one caught all the way (x the user's damage): I 8, II 9, III 10 (+ the burst at
    /// its end 2.5).
    /// </summary>
    internal sealed class FinalRadianceTech : Technique
    {
        public override string Id => "final_radiance";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Line;
        public override School School => School.KiArts;
        public override Form Forms => Form.Beam | Form.Charge | Form.Ultimate;
        public override Scale Scale => Scale.Ultimate;
        public override float[] ManaCost => new[] { 300f, 400f, 500f };
        public override float[] Cooldown => new[] { 80f, 85f, 90f };

        public static float Length(int level)
        {
            return level <= 1 ? 28f : level == 2 ? 32f : 36f;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 8f : level == 2 ? 9f : 10f;
        }

        private static float ChargeTime(int level)
        {
            return level <= 1 ? 1.5f : level == 2 ? 1.6f : 1.7f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            // a beam already coming at it: the answer of answers (it wins the clash)
            Beam incoming = Beams.Toward(c.Self, 1.5f);
            if (incoming != null && incoming.Spec.Clashes && incoming.A != null && Combat.IsFoe(c.Self, incoming.A) && c.Allowed(incoming.A))
            {
                float d = Vector2.Distance(c.Pos, incoming.A.current_position);
                bool rival = incoming.Spec.AlwaysWins;   // (another Final Radiance: a true clash of equals)
                if (d <= Length(level) * 1.1f && d >= 1.5f && (rival || Tiers.Of(incoming.A) >= Tier.Master || c.Forced))
                {
                    Vector2 dir = (incoming.A.current_position - c.Pos).normalized;
                    return new Plan
                    {
                        Target = incoming.A,
                        Point = incoming.A.current_position,
                        Dir = dir,
                        Count = 1,
                        Score = 400f + c.Value(incoming.A, c.Expected(incoming.A, Mult(level))),
                        Data = c.Forced ? (object)true : "answer"
                    };
                }
            }
            Plan p = Choreo.PickLine(c, this, level, Length(level), 1.3f, Mult(level), 1, true);
            if (p == null || c.Forced)
            {
                return p;
            }
            if (c.Dist(p.Target) < 3f)
            {
                return null;   // the longest gather of all, never in arm's reach
            }
            // the beam of beams is for an army, a peer, a giant, or the fight of its life
            bool worth = p.Count >= 5 || c.ThreatAround >= 1f || Tiers.Of(p.Target) >= Tier.Grandmaster || Weights.Species(p.Target) >= Weight.Titan
                || c.Desperate && c.Threat(p.Target) >= 0.4f;
            if (!worth)
            {
                return null;
            }
            p.Score *= 1f + 0.04f * Mathf.Min(15, p.Count);
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            bool answer = p.Data is string s && s == "answer";
            Radiance st = new Radiance();
            m.Ended2 = (mm, aborted) => st.Clear();
            m.Face(p.Dir);
            float rise = 0.38f + 0.05f * level;
            Form charging = Form.Beam | Form.Charge | Form.Ultimate;
            Form firing = Form.Beam | Form.Ultimate;
            m.OpenCinematic(8f, 0.5f, "sakuga_title_final_radiance");
            // it rises off the ground in a column of its own light
            m.Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = answer ? 0.12f : 0.3f,
                To = new Shape(-4f, 0.92f, 1.1f, rise),
                Ease = Ease.InOutSine,
                Charging = true,
                Forms = charging,
                Enter = mm => st.Lift(mm),
                Every = (mm, k) => st.Rising(mm, k)
            });
            // every mote of light on the field streams into its body
            m.Charge(ChargeTime(level) * (answer ? 0.3f : 1f), ChargeStyle.Stance, 4.5f, Element.Holy);
            m.Last.To = new Shape(4f, 1.1f, 0.92f, rise);
            m.Every((mm, k) => st.Gather(mm, k, level));
            // the release
            m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.07f,
                To = new Shape(14f, 1.26f, 0.84f, rise, 0.08f),
                Ease = Ease.OutExpo,
                Forms = firing,
                Enter = mm => st.Fire(mm, level)
            });
            // the beam pours out of it; it is pushed back through the air by it
            m.Add(new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.25f,
                To = new Shape(9f, 1.18f, 0.88f, rise, 0.05f),
                Ease = Ease.OutQuad,
                Track = false,
                Forms = firing,
                Until = mm => st.Beam == null || st.Beam.Done || !st.Beam.Firing,
                MaxDur = 6f,
                Every = (mm, k) => st.Recoil(mm),
                Exit = mm => st.Finale(mm, level)
            });
            m.Recover(0.42f);
            return m.Start();
        }

        /// <summary>The bits of the gather and the beam.</summary>
        private sealed class Radiance
        {
            public Beam Beam;
            private Particle _pillar, _aura;
            private float _rockT, _moteT, _shakeT, _dustT;
            private Vector2 _aim = Vector2.right;

            /// <summary>It leaves the ground: the earth cracks under it.</summary>
            public void Lift(ChoreoMove m)
            {
                Choreo.Sound("event:/SFX/POWERS/Powerup", m.A.current_tile);
                Choreo.Sound("event:/SFX/NATURE/EarthQuake", m.A.current_tile, 0.2f);
                if (m.Vis)
                {
                    WorldTile t = m.A.current_tile;
                    Kit.Crater(m.Pose.Ground, m.Px * m.Kk * 1.3f, t, false, 4f, false);
                    Kit.DustRing(m.Pose.Ground, m.Px, t, 14, 50f, 0.6f, m.Kk);
                    Fx.Ring(m.Pose.Ground, m.Px, 0.4f, 3f, 22f * m.Kk, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.2f, Fx.WithA(m.Hue.Core, 0.95f), Fx.WithA(m.Hue.Rim, 0f));
                }
            }

            public void Rising(ChoreoMove m, float k)
            {
                Column(m, 0.3f * k);
                Rocks(m, k);
            }

            /// <summary>The gather: motes from far away, its body shining brighter, the rocks lifting, the ground shaking.</summary>
            public void Gather(ChoreoMove m, float k, int level)
            {
                Column(m, 0.3f + 0.5f * k);
                Rocks(m, 0.6f + 0.4f * k);
                Hue h = m.Hue.As(Element.Holy);
                if (m.Vis)
                {
                    if (_aura == null)
                    {
                        _aura = m.Glue(Fx.Spawn(Art.Disk(Mathf.Clamp(Mathf.RoundToInt(m.Hpx * 0.8f), 3, 60), 1f, 0.6f), m.BodyPos(), m.Px, 1f, FxLayer.Behind, 2));
                    }
                    if (_aura != null && _aura.Alive)
                    {
                        _aura.Pos = m.BodyPos();
                        _aura.Size0 = _aura.Size1 = 0.6f + 0.9f * k + 0.08f * Mathf.Sin(m.T * 37f);
                        _aura.Grad = null;
                        _aura.R.color = Fx.WithA(Color.Lerp(h.Edge, Color.white, k), 0.25f + 0.45f * k);
                        _aura.Cine = m.Cinematic;
                    }
                    _moteT -= Clock.Dt;
                    if (_moteT <= 0f)
                    {
                        _moteT = 0.016f;
                        // from far across the field
                        ChoreoFx.Motes(m.BodyPos(), m.Px, 3, (40f + 20f * k) * m.Kk, 0.24f, h.Core, h.Edge, m.TopLayer);
                    }
                }
                _shakeT -= Clock.Dt;
                if (_shakeT <= 0f)
                {
                    _shakeT = 0.3f;
                    Ruin.Quake(m.A, m.Pose.Ground, Scale.Heavy, 0.5f + k, m.Kk);
                    if (m.Vis && k > 0.4f)
                    {
                        Kit.DustRing(m.Pose.Ground, m.Px, m.A.current_tile, 8, -40f, 0.5f, m.Kk);
                    }
                }
            }

            /// <summary>A column of light from the ground through it up into the sky.</summary>
            private void Column(ChoreoMove m, float a)
            {
                if (!m.Vis)
                {
                    return;
                }
                Vector2 g = m.Pose.Ground;
                Hue h = m.Hue.As(Element.Holy);
                KiHi.Bar(ref _pillar, g, g + new Vector2(0f, 9f * m.Kk), m.Px, (4f + 6f * a) * m.Kk, Fx.WithA(Color.Lerp(h.Edge, Color.white, a), 0.35f * a + 0.05f * Mathf.Sin(m.T * 29f)), FxLayer.Behind, 1);
                if (_pillar != null)
                {
                    _pillar.Cine = m.Cinematic;
                }
            }

            /// <summary>Rocks torn loose rising slowly into the air round it.</summary>
            private void Rocks(ChoreoMove m, float k)
            {
                if (!m.Vis)
                {
                    return;
                }
                _rockT -= Clock.Dt;
                if (_rockT > 0f)
                {
                    return;
                }
                _rockT = Mathf.Lerp(0.12f, 0.05f, k);
                WorldTile t = m.A.current_tile;
                Color soil = Fx.SoilColor(t);
                Vector2 at = m.Pose.Ground + new Vector2(Random.Range(-1.6f, 1.6f), Random.Range(-0.8f, 0.8f)) * m.Kk;
                Particle r = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], at, m.Px, Random.Range(0.9f, 1.4f), FxLayer.Top, 4);
                if (r != null)
                {
                    r.Moving(Vector2.zero, Random.Range(2f, 6f) * m.Px, 0f, -Random.Range(10f, 22f) * m.Px);
                    r.Spin = Random.Range(-120f, 120f);
                    r.Cine = m.Cinematic;
                    r.Color(soil, soil, Fx.WithA(soil, 0f));
                }
            }

            /// <summary>The release: the light leaves it all at once.</summary>
            public void Fire(ChoreoMove m, int level)
            {
                m.SpendOrb();
                Vector2 aim = m.V != null && m.VOk ? m.VG - m.Stance : m.Aim;
                _aim = aim.sqrMagnitude > 1e-4f ? aim.normalized : new Vector2(m.Fwd, 0f);
                m.Face(_aim);
                Vector2 at = m.Muzzle();
                Beam = Beams.Fire(m.A, m, at, _aim, new BeamSpec
                {
                    Length = Length(level),
                    Width = level <= 1 ? 14f : level == 2 ? 16f : 18f,
                    Speed = 50f,
                    Life = level <= 1 ? 1.2f : level == 2 ? 1.35f : 1.5f,
                    Mult = Mult(level),
                    Push = level <= 1 ? 3.5f : level == 2 ? 4f : 4.5f,
                    Trench = level <= 1 ? 1.4f : level == 2 ? 1.6f : 1.8f,
                    Power = 1.6f,
                    AlwaysWins = true,
                    Element = Element.Holy
                });
                WorldTile tile = m.A.current_tile;
                Choreo.Sound("event:/SFX/POWERS/DivineLight", tile, 0.1f);
                Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", tile);
                Choreo.Sound("event:/SFX/POWERS/HeatRayMelts", tile, 0.3f);
                if (m.Vis)
                {
                    float px = m.Px;
                    Kit.ImpactFrame(at, px, 1.5f * m.Kk, m.Hue.Pal, false, m.TopLayer);
                    Kit.Flash(at, px, Mathf.RoundToInt(12f * m.Kk), 0.12f, Color.white, m.TopLayer);
                    Fx.Ring(at, px, 0.3f, 4f, 30f * m.Kk, 3, 1, m.TopLayer, 6, 1.2f, 2.4f, Fx.WithA(Color.white, 1f), Fx.WithA(m.Hue.Edge, 0.6f), Fx.WithA(m.Hue.Rim, 0f));
                    Fx.FocusLines(at, px, Fx.Count(18), 14f * m.Kk, 30f * m.Kk, 0.18f);
                    Kit.DustRing(m.Pose.Ground, px, tile, 20, 90f, 0.8f, m.Kk * 1.3f);
                }
                KiHi.Kill(ref _pillar);
                if (_aura != null)
                {
                    m.Unglue(_aura);
                    _aura = null;
                }
                // the ground under it is blasted by the release
                Ruin.Blast(m.A, m.Pose.Ground, 1f, Scale.Heavy, m.Pose.Ground);
                Ruin.Quake(m.A, m.Pose.Ground, Scale.Ultimate, 1.6f, m.Kk);
                Kit.Stop(at, 0.1f);
                if (m.Cinematic)
                {
                    Cinema.SlowMo(0.35f, 0.45f);
                }
            }

            /// <summary>The beam pours out: it is pushed back through the air, trembling, the ground shaking under it.</summary>
            public void Recoil(ChoreoMove m)
            {
                float dt = Clock.Dt;
                bool strain = Beam != null && Beam.Clash != null;
                Vector2 back = m.Stance - _aim * dt * (strain ? 0.7f : 0.3f) * m.Kk;
                if (Combat.SafeSpot(back))
                {
                    m.Stance = back;
                }
                m.Pose.Nudge = Choreo.Tremble(m.BeatT, strain ? 1f : 0.55f);
                _dustT -= dt;
                if (_dustT <= 0f && m.Vis)
                {
                    _dustT = 0.06f;
                    ChoreoFx.HeelDust(m.Pose.Ground, -_aim, m.Px, m.A.current_tile, m.Kk);
                    Particle g = Fx.Ghost(m, new Vector2(Random.Range(-1f, 1f), Random.Range(0f, 1.5f)) * m.Px, 0f, 0.14f, Fx.WithA(m.Hue.Edge, 0.5f), Fx.WithA(m.Hue.Edge, 0f));
                    if (g != null)
                    {
                        g.Sized(1f, 1.1f);
                    }
                }
                _shakeT -= dt;
                if (_shakeT <= 0f)
                {
                    _shakeT = 0.25f;
                    Ruin.Quake(m.A, m.Pose.Ground, Scale.Ultimate, 0.6f, m.Kk);
                }
            }

            /// <summary>Where the beam ends the earth bursts (unless it was caught, cut short, or lost its clash).</summary>
            public void Finale(ChoreoMove m, int level)
            {
                Beam b = Beam;
                if (b == null || b.Lost || b.Len < b.MaxLen * 0.9f)
                {
                    return;
                }
                Vector2 at = Combat.Clamp(b.GroundTip);
                WorldTile tile = Combat.TileAt(at);
                Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", tile);
                if (Kit.Visible(at, 0.5f))
                {
                    float px = m.Px;
                    Kit.ImpactFrame(at + new Vector2(0f, 0.8f * m.Kk), px, 1.6f * m.Kk, m.Hue.Pal, false);
                    Kit.Flash(at + new Vector2(0f, 0.6f * m.Kk), px, Mathf.RoundToInt(22f * m.Kk), 0.16f, Color.white);
                    Fx.Ring(at, px, 0.5f, 5f, 44f * m.Kk, 3, 1, FxLayer.Ground, 6, Art.Ground, 2.2f, Fx.WithA(Color.white, 1f), Fx.WithA(m.Hue.Edge, 0.7f), Fx.WithA(m.Hue.Rim, 0f));
                    Kit.Crater(at, px * m.Kk * 1.6f, tile, true, 9f, false, m.Hue.Edge);
                    Kit.Debris(at, px, tile, 18, m.Kk * 1.3f);
                    Kit.DustRing(at, px, tile, 18, 80f, 0.7f, m.Kk);
                }
                Ruin.Blast(m.A, at, 1.6f, Scale.Ultimate, m.Pose.Ground);
                Ruin.Quake(m.A, at, Scale.Ultimate, 1.5f, m.Kk);
                Kit.Stop(at, 0.06f);
                m.AreaBlow(at, (3f + 0.5f * level) * Mathf.Lerp(1f, m.Kk, 0.5f), 2.5f, null, Hurt.Launch, 2.6f, 1.6f);
            }

            public void Clear()
            {
                KiHi.Kill(ref _pillar);
                KiHi.Kill(ref _aura);
            }
        }
    }
}
