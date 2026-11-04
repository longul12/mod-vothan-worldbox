using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// REFLECT BARRIER (Ki Arts, Adept). Both palms thrust out and a hexagon of gold light snaps into
    /// being in front of it. A shot that strikes it (a ki bullet, an orb, a disc, a boulder, the
    /// game's own arrows and fireballs) ripples the hexagon and flies straight back at the one who
    /// threw it, now the reflector's own (I as strong, II x1.2, III x1.5 and bigger); a beam splashes
    /// against it and is held there, the reflector braced and pushed back along the ground, until the
    /// beam dies or overwhelms it (a far mightier beam breaks through sooner: the hexagon shatters and
    /// the reflector is blasted away). While it stands it is a guard (it shows Form.Guard): punches,
    /// palms and blades from the front clang off it (2 / 3 / 5 of them, then it breaks), but a kick
    /// or a grab smashes straight through it ("they curl up, you kick them").
    ///
    /// Reactive: a shot or a beam reaching a free fighter that reads it raises the barrier for the
    /// rest of the volley (0.35 / 0.45 / 0.55 s). Planned: a foe gathering a beam or a shot aimed at
    /// it, a beam already coming, shots in flight: it raises the barrier ahead of time (up to
    /// 1 / 1.4 / 1.8 s; lowered once nothing more comes). The game's projectiles: a fighter that
    /// knows it may raise it as an arrow arrives, and the game itself sends the arrow back.
    /// </summary>
    internal sealed class ReflectBarrierTech : Technique
    {
        public override string Id => "reflect_barrier";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;
        public override School School => School.KiArts;
        public override Form Forms => Form.Ki | Form.Guard;
        public override Form Answers => Form.Shot;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 10f, 16f, 24f };
        public override float[] Cooldown => new[] { 6f, 7f, 8f };

        private const string Timer = "sakuga_reflect_read";

        private static ReflectBarrierTech _self;

        public ReflectBarrierTech()
        {
            _self = this;
        }

        [SakugaInit]
        private static void Init()
        {
            Shots.Interceptors.Add(Intercept);
            Combat.ContactHandlers.Add(OnContact);
            Hooks.BeforeOrdinaryHit.Add(OnOrdinary);
            Hooks.WorldCleared += ReflectBarrierMove.Forget;
            // (while the barrier stands its own Brain waits behind it)
            Hooks.HoldTechniques.Add(a => Moves.TryGet(a, out Move m) && m is ReflectBarrierMove bm && bm.A == a && bm.Up);
        }

        /// <summary>
        /// Dev: "kishoot TECH [LEVEL] [force|-] [DELAY]": the performer's nearest foe uses TECH on it (a
        /// shot, a volley, a beam: what a barrier is for; a bullet or a flare at a performer charging a
        /// beam: an interrupt from afar) after DELAY seconds; "force": the performer always reads it and
        /// answers with the barrier.
        /// </summary>
        [SakugaInit]
        private static void DevInit()
        {
            Dev.Commands["kishoot"] = args =>
            {
                float delay = Dev.ArgF(args, 4, 0f);
                if (delay > 0f)
                {
                    _devAt = Time.unscaledTime + delay;
                    _devArgs = args;
                    return;
                }
                Shoot(args);
            };
            Hooks.FrameTick += dt =>
            {
                if (_devArgs != null && Time.unscaledTime >= _devAt)
                {
                    string[] a = _devArgs;
                    _devArgs = null;
                    try
                    {
                        Shoot(a);
                    }
                    catch (System.Exception e)
                    {
                        Dev.Log("kishoot failed: " + e.Message);
                    }
                }
            };
            // Dev: "kiarts": every tiered demo unit walks the Ki Arts and no other school (after 'battle'/'war': the AI fights with them)
            Dev.Commands["kiarts"] = args =>
            {
                int n = 0;
                foreach (Actor u in Dev.Units)
                {
                    if (u == null || !u.isAlive() || Tiers.Of(u) == Tier.None)
                    {
                        continue;
                    }
                    for (int i = 1; i <= Schools.Count; i++)
                    {
                        ActorTrait tr = Schools.Trait((School)i);
                        if (tr != null && (School)i != School.KiArts && u.hasTrait(tr))
                        {
                            u.removeTrait(tr);
                        }
                    }
                    if (Schools.Trait(School.KiArts) != null)
                    {
                        u.addTrait(Schools.Trait(School.KiArts), true);
                    }
                    n++;
                }
                Dev.Log("kiarts: " + n + " fighters walk the Ki Arts");
            };
        }

        private static string[] _devArgs;
        private static float _devAt;

        private static void Shoot(string[] args)
        {
            Actor perf = Dev.Performer;
            Technique t = Techniques.Get(Dev.Arg(args, 1, "ki_bullet"));
            if (perf == null || t == null)
            {
                Dev.Log("kishoot: no performer or no such technique");
                return;
            }
            int level = Mathf.Clamp((int)Dev.ArgF(args, 2, 2f), 1, 3);
            Actor shooter = null;
            float bd = float.MaxValue;
            foreach (Actor u in Dev.Units)
            {
                if (u == null || u == perf || !u.isAlive() || !Combat.IsFoe(u, perf))
                {
                    continue;
                }
                float d = (u.current_position - perf.current_position).sqrMagnitude;
                if (d < bd)
                {
                    bd = d;
                    shooter = u;
                }
            }
            if (shooter == null)
            {
                Dev.Log("kishoot: no foe of the performer");
                return;
            }
            Tier tier = (Tier)Mathf.Clamp((int)t.Entry + level - 1, 1, 6);
            if (Tiers.Of(shooter) < tier)
            {
                Progression.Promote(shooter, tier, false);
            }
            if (t.School != School.None && Schools.Trait(t.School) != null)
            {
                shooter.addTrait(Schools.Trait(t.School), true);
            }
            if (Moves.TryGet(shooter, out Move busy) && busy.A == shooter)
            {
                Moves.Cancel(busy);   // (a promotion's ceremony: the shot comes now)
            }
            if (Dev.Arg(args, 3, "") == "force")
            {
                Reactions.DevForce[perf.Sid()] = "reflect_barrier";
            }
            bool ok = Brain.Force(shooter, t, level, perf, null);
            Dev.Log("kishoot: " + shooter.asset.id + " " + t.Id + " " + Techniques.Roman(level) + " at the performer: " + ok);
        }

        // -------------------------------------------------------------- planned --

        public override Plan Plan(Ctx c, int level)
        {
            Actor threat = null;
            float worth = 0f;
            // a beam already coming at it
            Beam b = Beams.Toward(c.Self, 0.6f);
            if (b != null && b.Clash == null && b.A != null && Combat.Living(b.A) && Combat.IsFoe(c.Self, b.A))
            {
                threat = b.A;
                worth = c.D * 2f + 40f + 20f * Tiers.Power(b.A);
            }
            // a foe gathering a beam or a shot aimed at it (not one at arm's length: hit that one instead)
            if (threat == null)
            {
                for (int i = 0; i < c.Enemies.Count && i < 12; i++)
                {
                    float d = c.EnemyDist[i];
                    if (d > 16f)
                    {
                        break;
                    }
                    Actor v = c.Enemies[i];
                    if (d < 2.5f || !Moves.Charging(v))
                    {
                        continue;
                    }
                    FoeState st = c.State(v);
                    if ((st.Doing & (Form.Beam | Form.Projectile)) == 0 || !st.AimsAtMe)
                    {
                        continue;
                    }
                    float w = c.D * 2f + 30f + 20f * Tiers.Power(v);
                    if (w > worth)
                    {
                        worth = w;
                        threat = v;
                    }
                }
            }
            // shots in flight at it
            if (threat == null)
            {
                Shot s = Shots.Incoming(c.Self, 4f);
                if (s != null && s.A != null && Combat.Living(s.A) && s.Spec.Mult * c.D > c.Raw * 0.5f)
                {
                    threat = s.A;
                    worth = c.D * (1f + s.Spec.Mult) + 15f;
                }
            }
            if (threat == null && c.Forced)
            {
                threat = c.ForceTarget ?? c.Focus ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
            }
            if (threat == null)
            {
                return null;
            }
            Vector2 dir = threat.current_position - c.Pos;
            return new Plan
            {
                Target = threat,
                Point = threat.current_position,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = 1,
                Score = c.Forced ? Mathf.Max(worth, 1f) : worth,
                Data = c.Forced
            };
        }

        public override bool Begin(Actor a, Plan p)
        {
            return ReflectBarrierMove.Raise(a, p.Target, this, p.Level, true) != null;
        }

        // -------------------------------------------------------------- reactive --

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            // only what it is made for: a shot of the engine's, a ki bolt, a beam (a ki palm is "Ki" too, and no shot)
            Shot s = Shots.Reaching;
            bool shot = s != null && !s.Done && s.Hostile && s.A == attacker;
            bool bolt = !shot && area && incoming is KiShot;
            Beam beam = !shot && !bolt && !area ? ReflectBarrierMove.BeamOf(attacker, defender) : null;
            if (!shot && !bolt && beam == null || area && !bolt)
            {
                return Contact.Hit;
            }
            if (!Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            Reactions.Free(defender);
            ReflectBarrierMove m = ReflectBarrierMove.Raise(defender, attacker, this, level, false);
            if (m == null)
            {
                return Contact.Hit;
            }
            if (shot)
            {
                m.Turn(s);
                return Contact.Parried;
            }
            if (bolt)
            {
                m.Glance(attacker.current_position);
                return Contact.Evaded;
            }
            m.HoldBeam(beam);
            return Contact.Parried;
        }

        // ------------------------------------------------------- the barrier up --

        /// <summary>Shots.Interceptors: a shot reaching a raised barrier from the front is sent back.</summary>
        private static bool Intercept(Shot s, Actor u)
        {
            if (s == null || u == null || s.Done || !s.Hostile || !Moves.TryGet(u, out Move mv) || !(mv is ReflectBarrierMove bm) || !bm.Up || bm.A != u)
            {
                return false;
            }
            if (!bm.Facing(s.Pos - s.Dir * 2f))
            {
                return false;   // (from behind it: the barrier faces the other way)
            }
            return bm.Turn(s);
        }

        /// <summary>
        /// Combat.ContactHandlers: a blow reaching a raised barrier. A beam is held at it; a ki bolt
        /// glances off; punches, palms and blades from the front clang off it; kicks and grabs (and
        /// heavy blows from above) smash it and land.
        /// </summary>
        private static Contact OnContact(Move m, Actor v, bool area)
        {
            if (m == null || v == null || m.A == v || !Moves.TryGet(v, out Move mv) || !(mv is ReflectBarrierMove bm) || !bm.Up || bm.A != v)
            {
                return Contact.Hit;
            }
            Form f = Reads.FormsOf(m);
            if ((f & Form.Beam) != 0)
            {
                Beam b = ReflectBarrierMove.BeamOf(m.A, v);
                if (b != null && bm.Facing(b.From))
                {
                    bm.HoldBeam(b);
                    return Contact.Parried;
                }
                return Contact.Hit;
            }
            if (m is KiShot)
            {
                if (!bm.Facing(m.A.current_position))
                {
                    return Contact.Hit;
                }
                bm.Glance(m.A.current_position);
                return Contact.Evaded;
            }
            if (area || m.A == null)
            {
                return Contact.Hit;   // an area blow washes round it
            }
            if ((f & (Form.Grab | Form.Throw | Form.Kick)) != 0 || (f & Form.Heavy) != 0 && (f & Form.Overhead) != 0)
            {
                bm.Smash(m.A);
                return Contact.Hit;
            }
            if ((f & (Form.Strike | Form.Shot)) == 0 || !bm.Facing(m.A.current_position))
            {
                return Contact.Hit;
            }
            bm.Clang(m.A.current_position);
            return Contact.Parried;
        }

        /// <summary>
        /// Hooks.BeforeOrdinaryHit: the game's own attacks. A raised barrier turns arrows and spells
        /// back (the game sends them) and plain blows from the front aside; a fighter who knows it and
        /// reads an arrow coming may raise it there and then.
        /// </summary>
        private static bool OnOrdinary(AttackData d, Actor t)
        {
            if (t == null || d.initiator == null || !d.initiator.isActor())
            {
                return false;
            }
            Actor atk = d.initiator.a;
            if (atk == null || atk == t || !Combat.Living(atk))
            {
                return false;
            }
            Vector2 from = d.is_projectile ? (Vector2)d.initiator_position : atk.current_position;
            if (Moves.TryGet(t, out Move mv) && mv is ReflectBarrierMove bm && bm.Up && bm.A == t)
            {
                if (!bm.Facing(from))
                {
                    return false;
                }
                if (d.is_projectile)
                {
                    bm.Ripple(true);
                    return true;   // (the game sends the shot back where it came from)
                }
                bm.Clang(from);
                return true;
            }
            if (!d.is_projectile || _self == null || !Fighters.TryGet(t, out Fighter f))
            {
                return false;
            }
            int level = Techniques.Level(t, _self, f.Tier);
            if (level <= 0 || !f.Ready(_self) || !global::Sakuga.Mana.Has(t, _self, level, false) || !Moves.CanPerform(t) || !f.Elapsed(Timer, 0.5f))
            {
                return false;
            }
            f.Mark(Timer);
            if (Random.value >= 0.7f * Reactions.ReadChance(t, f, atk, null, false, 1f, 1))
            {
                return false;
            }
            ReflectBarrierMove m = ReflectBarrierMove.Raise(t, atk, _self, level, false);
            if (m == null)
            {
                return false;
            }
            // (answered outside the dispatch: it pays and is remembered here)
            global::Sakuga.Mana.Spend(t, _self, level, false);
            f.SetCooldown(_self, _self.CooldownFor(t, level));
            Director.Used(_self, t.current_position);
            Brain.Started.TryGetValue(_self.Id, out int n);
            Brain.Started[_self.Id] = n + 1;
            if (Dev.On) Dev.Trace("react reflect_barrier " + Techniques.Roman(level) + " by " + t.asset.id + "#" + t.Sid() + " turns " + atk.asset.id + "'s " + d.projectile_id + " back");
            try
            {
                Hooks.Answered?.Invoke(t, atk, _self, 0, Contact.Parried);
            }
            catch (System.Exception e)
            {
                SakugaMod.Report("reflect answered", e);
            }
            m.Ripple(true);
            return true;
        }
    }

    /// <summary>
    /// The barrier itself: a hexagon of light held in front of the fighter, facing the threat. It
    /// turns shots (Turn), holds beams (HoldBeam), lets ki bolts glance off (Glance), takes a few
    /// blows (Clang) and breaks under a kick or a grab (Smash) or a beam too strong for it.
    /// </summary>
    internal sealed class ReflectBarrierMove : ChoreoMove
    {
        /// <summary>The barrier is raised: shots turn, blows clang off.</summary>
        public bool Up;

        private readonly bool _stance;
        private readonly Actor _threat;
        private readonly long _threatId;
        private int _blocks;
        private Beam _beam;
        private float _beamFor, _beamMax, _quiet, _flash, _sparkT, _turn;
        private bool _blocked, _shattered, _breakNext;
        private Particle _hex, _inner, _fill;
        private Vector2 _hexAt;

        /// <summary>
        /// While it stands it is a light move to anything that takes hold of it (a grab tears the body out
        /// from behind the hexagon; a blow from behind knocks it out of its guard): only what meets the
        /// hexagon from the front is stopped.
        /// </summary>
        public override bool Interruptible => base.Interruptible || Up && !Finished && !Ended && !Broken;

        /// <summary>Taken out of it (a grab, a blow from behind, a daze): the hexagon breaks.</summary>
        public override void Abort()
        {
            if (Up)
            {
                ShatterFx();
            }
            base.Abort();
        }

        /// <summary>
        /// Shots barriers already sent back, and how often: turned again they grow no stronger, and the
        /// third time a barrier only knocks it away (a rally between two barriers, not a runaway).
        /// </summary>
        private static readonly Dictionary<Shot, int> _bounced = new Dictionary<Shot, int>();

        public static void Forget()
        {
            _bounced.Clear();
        }

        private ReflectBarrierMove(Actor a, Technique t, Plan p, bool stance, Actor threat) : base(a, t, p)
        {
            _stance = stance;
            _threat = threat;
            _threatId = threat != null ? threat.Sid() : -1;
            _blocks = Level <= 1 ? 2 : Level == 2 ? 3 : 5;
        }

        /// <summary>
        /// Raises the barrier against 'threat' (stance: ahead of time, held until nothing more comes;
        /// else a quick answer for the rest of a volley). Null if the fighter can't act now.
        /// </summary>
        public static ReflectBarrierMove Raise(Actor a, Actor threat, Technique t, int level, bool stance)
        {
            if (a == null || !Moves.CanPerform(a))
            {
                return null;
            }
            Vector2 at = threat != null ? threat.current_position : a.current_position + new Vector2(a.flip ? 1f : -1f, 0f);
            Vector2 d = at - a.current_position;
            // (no target: the barrier pins nobody, it only faces the threat)
            Plan p = new Plan
            {
                Tech = t,
                Level = Mathf.Clamp(level, 1, 3),
                Point = at,
                Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right,
                Count = 1
            };
            ReflectBarrierMove m = new ReflectBarrierMove(a, t, p, stance, threat);
            m.Script();
            if (!m.Start())
            {
                return null;
            }
            // (up at once: the shot or the beam that called for it is met this very frame)
            m.Vis = Fx.OnScreen(m.Pose.Ground, 0.35f);
            m.Raised();
            return m;
        }

        private static float SnapTime(int level)
        {
            return level <= 1 ? 0.35f : level == 2 ? 0.45f : 0.55f;
        }

        private static float StanceTime(int level)
        {
            return level <= 1 ? 1f : level == 2 ? 1.4f : 1.8f;
        }

        private static float BeamTime(int level)
        {
            return level <= 1 ? 0.9f : level == 2 ? 1.3f : 1.8f;
        }

        private static float Boost(int level)
        {
            return level <= 1 ? 1f : level == 2 ? 1.2f : 1.5f;
        }

        private void Script()
        {
            Form shows = Form.Ki | Form.Guard;
            Face(Point - Stance);
            // RAISE: both palms thrust out and the hexagon snaps into being
            PoseTo(0.06f, new Shape(6f, 1.1f, 0.92f, 0f, 0.04f), Ease.OutExpo, Tempo.Act).Shows(shows).Every((m, k) => PlaceHex(Clock.Dt));
            Last.Track = false;
            // HOLD: braced behind it, facing the threat
            Beat hold = new Beat
            {
                Tempo = Tempo.Free,
                Dur = _stance ? StanceTime(Level) : SnapTime(Level),
                To = new Shape(-4f, 1.08f, 0.92f, 0f, -0.02f),
                Ease = Ease.OutQuad,
                Forms = shows,
                Track = false
            };
            hold.Every = (m, k) => HoldTick();
            Add(hold);
            // LOWER: it folds back into the palms
            PoseTo(0.1f, new Shape(2f, 1.02f, 0.98f, 0f, 0f), Ease.OutQuad, Tempo.Follow).Shows(shows).Label("lower")
                .OnlyIf(m => !_shattered).OnEnter(m => Lower());
            Last.Track = false;
            // SHATTER: shards fly, it reels back a step
            PoseTo(0.14f, new Shape(-16f, 0.94f, 1.06f, 0f, -0.1f), Ease.OutQuad, Tempo.Follow).Shows(shows).Label("shatter")
                .OnlyIf(m => _shattered);
            Last.Track = false;
            Recover(0.18f);
        }

        // ------------------------------------------------------------- the look --

        private float HexR => Hpx * (Level <= 1 ? 0.72f : Level == 2 ? 0.82f : 0.92f);

        /// <summary>In front of it, between it and the threat.</summary>
        private Vector2 HexPoint()
        {
            return BodyPos() + new Vector2(Dir.x, Dir.y * 0.5f) * Hpx * 0.45f * Px;
        }

        private void Raised()
        {
            Up = true;
            _hexAt = HexPoint();
            int r = Mathf.Max(4, Mathf.RoundToInt(HexR));
            _fill = Glue(Fx.Spawn(Art.Disk(Mathf.Max(2, Mathf.RoundToInt(r * 0.8f)), 1f, 0.7f), _hexAt, Px, 1f, FxLayer.Over, 8));
            _hex = Glue(Fx.Spawn(ChoreoArt.Hex(r, Level >= 2 ? 2 : 1), _hexAt, Px, 1f, FxLayer.Over, 10));
            if (Level >= 3)
            {
                _inner = Glue(Fx.Spawn(ChoreoArt.Hex(Mathf.Max(3, Mathf.RoundToInt(r * 0.6f)), 1), _hexAt, Px, 1f, FxLayer.Over, 11));
            }
            _flash = 0.1f;
            Choreo.Sound("event:/SFX/HIT/HitMetal", A.current_tile, 0.15f);
            Choreo.Sound("event:/SFX/POWERS/Powerup", A.current_tile, 0.2f);
            if (Vis)
            {
                Kit.Flash(_hexAt, Px, Mathf.RoundToInt(r * 0.5f), 0.07f, Fx.WithA(Hue.Core, 0.9f), TopLayer, 24);
                Fx.Ring(_hexAt, Px, 0.18f, r * 0.5f, r * 1.3f, 1, 1, TopLayer, 22, 1f, 2.4f, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
            }
            PlaceHex(0f);
        }

        private void PlaceHex(float dt)
        {
            _hexAt = HexPoint();
            _flash = Mathf.Max(0f, _flash - dt);
            _turn += dt * 40f;
            float pulse = 0.78f + 0.14f * Mathf.Sin(T * 11f);
            float hot = Mathf.Clamp01(_flash / 0.1f);
            Color line = Color.Lerp(Color.Lerp(Hue.Core, Hue.Edge, 0.35f), Color.white, hot);
            if (Level >= 2)
            {
                line = Color.Lerp(line, Hue.Rim, 0.18f * (1f - hot));
            }
            float sx = 0.82f + 0.08f * hot;
            Set(_hex, line, pulse + 0.2f * hot, 0f, sx);
            Set(_fill, Hue.Main, 0.16f + 0.25f * hot, 0f, sx);
            Set(_inner, Hue.Core, 0.65f * pulse + 0.3f * hot, _turn, sx);
        }

        private void Set(Particle p, Color c, float a, float rot, float sx)
        {
            if (p == null || !p.Alive)
            {
                return;
            }
            p.Pos = _hexAt;
            p.Grad = null;
            p.R.color = Fx.WithA(c, Mathf.Clamp01(a));
            p.Rot = rot;
            p.SX = sx;
        }

        /// <summary>The hexagon ripples where something struck it (a hex wave, a flash, sparks); 'turned': a shot sent back.</summary>
        public void Ripple(bool turned)
        {
            _flash = 0.1f;
            _blocked = true;
            _quiet = 0f;
            Choreo.Sound("event:/SFX/HIT/HitMetal", A.current_tile, 0.05f);
            if (!Vis)
            {
                return;
            }
            int r = Mathf.Max(4, Mathf.RoundToInt(HexR));
            Particle wave = Fx.Spawn(ChoreoArt.Hex(r, 1), _hexAt, Px, 0.2f, TopLayer, 23);
            if (wave != null)
            {
                wave.SX = 0.82f;
                wave.Sized(1f, 1.45f).Color(Fx.WithA(Color.white, 0.95f), Fx.WithA(Hue.Edge, 0.5f), Fx.WithA(Hue.Rim, 0f));
            }
            Kit.Flash(_hexAt, Px, Mathf.RoundToInt(r * 0.4f), 0.05f, Fx.WithA(Hue.Core, 0.9f), TopLayer, 25);
            Kit.Sparks(_hexAt, Px, turned ? 5 : 3, 40f, 100f, Hue.Edge, TopLayer);
            if (turned)
            {
                Fx.Ring(_hexAt, Px, 0.16f, 2f, r * 0.9f, 1, 1, TopLayer, 22, 1f, 2.2f, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
            }
        }

        private void Lower()
        {
            Up = false;
            if (Vis && _hex != null && _hex.Alive)
            {
                Particle fold = Fx.Spawn(ChoreoArt.Hex(Mathf.Max(4, Mathf.RoundToInt(HexR)), 1), _hexAt, Px, 0.12f, TopLayer, 22);
                if (fold != null)
                {
                    fold.SX = 0.82f;
                    fold.Sized(1f, 0.15f).Color(Fx.WithA(Hue.Core, 0.85f), Fx.WithA(Hue.Edge, 0f));
                }
            }
            DropHex();
        }

        private void DropHex()
        {
            Unglue(_hex);
            Unglue(_inner);
            Unglue(_fill);
            _hex = _inner = _fill = null;
        }

        /// <summary>The hexagon breaks: shards of light flung out, a crack of sound.</summary>
        private void ShatterFx()
        {
            Up = false;
            Choreo.Sound("event:/SFX/HIT/HitMetal", A.current_tile, 0.02f);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", A.current_tile, 0.1f);
            if (Vis)
            {
                float px = Px;
                Kit.Flash(_hexAt, px, Mathf.RoundToInt(HexR * 0.6f), 0.08f, Color.white, TopLayer, 26);
                int n = Fx.Count(8 + 2 * Level);
                for (int i = 0; i < n; i++)
                {
                    float a = (i + Random.Range(-0.3f, 0.3f)) / n * Mathf.PI * 2f;
                    Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    Particle shard = Fx.Spawn(Art.Sparks[Random.Range(0, Art.Sparks.Length)], _hexAt + d * HexR * 0.6f * px, px, Random.Range(0.25f, 0.45f), TopLayer, 24);
                    if (shard == null)
                    {
                        break;
                    }
                    shard.Moving(d * Random.Range(50f, 110f) * px * Kk, Random.Range(10f, 30f) * px, 3f, 140f * px);
                    shard.Spin = Random.Range(-720f, 720f);
                    shard.Sized(1.6f, 1f).Color(Color.white, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
                }
                Fx.Ring(_hexAt, px, 0.22f, 3f, HexR * 1.6f, 2, 1, TopLayer, 22, 1f, 2.4f, Fx.WithA(Color.white, 0.9f), Fx.WithA(Hue.Rim, 0f));
            }
            DropHex();
        }

        // ----------------------------------------------------------- the holding --

        private bool ThreatOk => _threat != null && _threat.Sid() == _threatId && Combat.Living(_threat);

        /// <summary>Is 'from' in front of it (the side the barrier faces)?</summary>
        public bool Facing(Vector2 from)
        {
            Vector2 d = from - Pose.Ground;
            return d.sqrMagnitude < 0.04f || Vector2.Dot(d, Dir) > -0.15f * d.magnitude;
        }

        private void HoldTick()
        {
            float dt = Clock.Dt;
            if (_breakNext)
            {
                _breakNext = false;
                _shattered = true;
                ShatterFx();
                Jump("shatter");
                return;
            }
            if (_beam != null && (!_beam.Firing || _beam.Done))
            {
                _beam = null;
            }
            // a beam on its way to it is met before it can pass it by
            if (_beam == null)
            {
                Beam b = Beams.Toward(A, 0.3f);
                if (b != null && b.Clash == null && Facing(b.From) && Vector2.Distance(b.GroundTip, Pose.Ground) < 2.2f * Kk)
                {
                    HoldBeam(b);
                }
            }
            if (_beam != null)
            {
                Face(-_beam.Dir);
            }
            else if (ThreatOk)
            {
                Vector2 d = _threat.current_position - Stance;
                if (d.sqrMagnitude > 0.01f)
                {
                    Face(d);
                }
            }
            PlaceHex(dt);
            if (_beam != null)
            {
                Holding(dt);
                return;
            }
            if (_stance)
            {
                // lowered once nothing more comes at it
                bool coming = Beams.Toward(A, 0.6f) != null || Shots.Incoming(A, 3.5f) != null || ThreatOk && Moves.Charging(_threat);
                _quiet = coming ? 0f : _quiet + dt;
                if (BeatT > 0.45f && _quiet > (_blocked ? 0.3f : 0.6f))
                {
                    Jump("lower");
                }
            }
        }

        /// <summary>A beam splashing against the hexagon: it holds it there, pushed back, until the beam dies or breaks through.</summary>
        public void HoldBeam(Beam b)
        {
            if (b == null || _beam == b || !Up)
            {
                return;
            }
            _beam = b;
            _beamFor = 0f;
            TierInfo ti = Tiers.Info(Tier);
            float mine = (ti != null ? ti.Might : 1f) * (0.8f + 0.2f * Level);
            _beamMax = BeamTime(Level) * Mathf.Clamp(mine / Mathf.Max(0.1f, b.Power) * 1.3f, 0.35f, 1.5f);
            b.StopAt(OnBeam(b), 0.2f);
            Ripple(false);
            if (Dev.On) Dev.Trace("reflect_barrier of " + A.asset.id + "#" + A.Sid() + " holds the beam of " + (b.A != null ? b.A.asset.id : "?") + " for up to " + _beamMax.ToString("F2") + " s");
        }

        /// <summary>Where the beam's line meets the hexagon.</summary>
        private Vector2 OnBeam(Beam b)
        {
            float along = Mathf.Max(0.3f, Vector2.Dot(_hexAt - b.From, b.Dir));
            return b.From + b.Dir * along;
        }

        private void Holding(float dt)
        {
            Beam b = _beam;
            _beamFor += dt;
            // (a beam held short of its length never reaches it: its own life is measured here, and it dies on the hexagon)
            if (b.Clash == null && b.Age >= b.MaxLen / Mathf.Max(5f, b.Spec.Speed * b.K) + b.Spec.Life + 0.1f)
            {
                b.Collapse();
                _beam = null;
                Ripple(true);
                if (Dev.On) Dev.Trace("reflect_barrier of " + A.asset.id + "#" + A.Sid() + " outlasts the beam");
                return;
            }
            _quiet = 0f;
            Vector2 at = OnBeam(b);
            b.StopAt(at, 0.2f);
            // the hold lasts as long as the beam does
            BeatDur = Mathf.Max(BeatDur, BeatT + 0.12f);
            // braced, trembling, pushed back along the ground
            Pose.Nudge = Choreo.Tremble(BeatT, 0.9f);
            Vector2 back = Stance + b.Dir * dt * 0.45f * Kk;
            if (Combat.SafeSpot(back))
            {
                Stance = back;
            }
            _sparkT -= dt;
            if (_sparkT <= 0f && Vis)
            {
                _sparkT = 0.04f;
                _flash = Mathf.Max(_flash, 0.05f);
                Kit.Sparks(at, Px, 3, 50f, 120f, Random.value < 0.5f ? Hue.Edge : b.Hue.Edge, TopLayer);
                Kit.Flash(at, Px, Mathf.RoundToInt(HexR * 0.35f), 0.04f, Fx.WithA(Color.Lerp(Hue.Core, b.Hue.Core, 0.5f), 0.9f), TopLayer, 25);
                ChoreoFx.HeelDust(Pose.Ground, -Dir, Px, A.current_tile, Kk);
                if (Level >= 3)
                {
                    // the beam glances off the hexagon in two thin rays
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector2 g = Choreo.Rotate(-b.Dir, 140f * s);
                        Kit.Line(at, at + g * 3f * Kk, Px, 0.05f, 1, TopLayer, 24, Fx.WithA(b.Hue.Core, 0.9f), Fx.WithA(b.Hue.Edge, 0f));
                    }
                }
            }
            if (_beamFor >= _beamMax)
            {
                Overwhelmed(b);
            }
        }

        /// <summary>The beam outlasts the barrier: it shatters, and the beam's power blasts the fighter away.</summary>
        private void Overwhelmed(Beam b)
        {
            _beam = null;
            _shattered = true;
            ShatterFx();
            Actor src = b.A;
            if (src != null && Combat.Living(src) && Combat.IsFoe(src, A) && Combat.Allowed(src, A, out float cm))
            {
                Combat.Hit(src, A, b.Spec.Mult * 0.35f * cm, false, false, 0f, b.M);
            }
            if (Dev.On) Dev.Trace("reflect_barrier of " + A.asset.id + "#" + A.Sid() + " is overwhelmed by the beam");
            BreakCharge(src, b.Dir, 2.2f + 0.5f * b.Spec.Push);
        }

        // -------------------------------------------------------------- answers --

        /// <summary>
        /// A shot strikes the hexagon: it ends there and its double flies straight back at whoever
        /// threw it (stronger from II), the reflector's own now; one that can't be sent back is knocked
        /// away or swallowed.
        /// </summary>
        public bool Turn(Shot s)
        {
            if (s == null || s.Done || !s.Hostile)
            {
                return false;
            }
            Ripple(true);
            Vector2 at = s.Pos + new Vector2(0f, s.Z);
            if (!s.Spec.Reflectable)
            {
                if (s.Spec.Deflectable)
                {
                    s.Deflect(Mirror(s.Dir));
                }
                else
                {
                    s.Done = true;
                }
                return true;
            }
            _bounced.TryGetValue(s, out int rally);
            _bounced.Remove(s);
            bool again = rally > 0;
            if (rally >= 2 && s.Spec.Deflectable)
            {
                s.Deflect(Mirror(s.Dir));   // (spent: it glances off and fades)
                return true;
            }
            Actor thrower = s.A;
            bool back = thrower != null && thrower.Sid() == s.AId && Combat.Living(thrower) && Combat.IsFoe(A, thrower);
            ShotSpec spec = (ShotSpec)s.Spec.MemberwiseCloneShot();
            spec.Delay = 0f;
            spec.Flight = back ? Flight.Homing : Flight.Straight;
            spec.Turn = 5f;
            spec.Speed = Mathf.Max(12f, s.Spec.Speed) * (again ? 1f : 1.1f + 0.1f * Level);
            spec.Mult = s.Spec.Mult * (again ? 1f : Boost(Level));
            spec.Size = s.Spec.Size * (Level >= 3 && !again ? 1.3f : 1f);
            spec.Range = back ? Vector2.Distance(s.Pos, thrower.current_position) + 5f : Mathf.Max(5f, s.Spec.Range - s.Traveled);
            Vector2 dir = back ? thrower.current_position - s.Pos : Mirror(s.Dir);
            s.Done = true;   // (the shot ends in the hexagon; its double flies back)
            Shot ns = Shots.Fire(A, this, at, dir, back ? thrower : null, Vector2.zero, spec, Hue);
            KiFlight.Watch(ns, A, Hue, KiFlight.Style.Reflected);
            if (ns != null)
            {
                if (_bounced.Count > 64)
                {
                    _bounced.Clear();
                }
                _bounced[ns] = rally + 1;
            }
            if (Dev.On) Dev.Trace("reflect_barrier of " + A.asset.id + "#" + A.Sid() + " sends a " + s.Spec.Kind + " back" + (back ? " at " + thrower.asset.id + "#" + thrower.Sid() : ""));
            return true;
        }

        /// <summary>The direction a shot bounces off the hexagon.</summary>
        private Vector2 Mirror(Vector2 d)
        {
            Vector2 r = d - 2f * Vector2.Dot(d, Dir) * Dir;
            return r.sqrMagnitude > 1e-4f ? r.normalized : -d;
        }

        /// <summary>A ki bolt glances off (it flies on past and fades).</summary>
        public void Glance(Vector2 from)
        {
            Ripple(false);
        }

        /// <summary>A blow from the front clangs off it; the last it can take breaks it right after.</summary>
        public void Clang(Vector2 from)
        {
            Ripple(false);
            if (--_blocks <= 0)
            {
                _breakNext = true;
            }
        }

        /// <summary>A kick or a grab smashes through: the hexagon shatters and the blow lands on a fighter free to feel it.</summary>
        public void Smash(Actor by)
        {
            if (!Up)
            {
                return;
            }
            _shattered = true;
            ShatterFx();
            if (Dev.On) Dev.Trace("reflect_barrier of " + A.asset.id + "#" + A.Sid() + " smashed by " + (by != null ? by.asset.id : "?"));
            Choreo.Kill(this);
        }

        /// <summary>The beam of 'shooter' whose line passes over 'v' (it is coming at it), if any.</summary>
        public static Beam BeamOf(Actor shooter, Actor v)
        {
            if (shooter == null || v == null)
            {
                return null;
            }
            for (int i = 0; i < Beams.All.Count; i++)
            {
                Beam b = Beams.All[i];
                if (b.A != shooter || !b.Firing || b.Clash != null)
                {
                    continue;
                }
                Vector2 rel = v.current_position - b.GroundFrom;
                float along = Vector2.Dot(rel, b.Dir);
                if (along >= 0f && along <= b.MaxLen + 1f && Mathf.Abs(rel.x * b.Dir.y - rel.y * b.Dir.x) <= b.Reach + 1f)
                {
                    return b;
                }
            }
            return null;
        }
    }
}
