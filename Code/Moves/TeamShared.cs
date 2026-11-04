using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// An ally taking part in someone else's team technique (Sky Relay, Cross Fang, Unison Burst).
    /// Like a Puppet it is posed every tick by the leader's move (its controller), but it is a
    /// performer: super armour in the air, intangible while it is a blur, and it keeps playing
    /// through a cinematic when its controller does. It lets go by itself if the controller ever
    /// ends without releasing it (it never stays Busy forever).
    /// </summary>
    internal sealed class TeamPartner : Move
    {
        /// <summary>The move that drives it (the leader's).</summary>
        public readonly Move Owner;

        /// <summary>A blur right now: nothing can touch it.</summary>
        public bool Blur;

        /// <summary>Weapons can't knock it out of the air.</summary>
        public bool Armor = true;

        private TeamHalo _glow;
        private readonly TeamHalo _lit;
        private Particle _flash;
        private float _flashLeft;
        private Color _flashColor = Color.white;

        public TeamPartner(Actor a, Move owner, bool faceRight) : base(a)
        {
            Owner = owner;
            Pose.FaceRight = faceRight;
            _glow = new TeamHalo(this, true, FxLayer.Over, 0);
            _lit = new TeamHalo(this, false, FxLayer.Cine, 6);
        }

        public override bool SuperArmor => Armor;

        public override bool Intangible => Blur;

        public override bool Cinematic => Owner != null && Owner.Cinematic;

        /// <summary>No longer ours: released, dead, recycled, picked up by the god hand, or possessed by the player.</summary>
        public bool Gone => Finished || A == null || A.Sid() != Id || !A.isAlive() || A.isInMagnet() || A.isInsideSomething() || ControllableUnit.isControllingUnit(A);

        /// <summary>The unit's glow (a flat-colour silhouette over it); alpha 0 hides it.</summary>
        public Color GlowColor
        {
            get => _glow.Color;
            set => _glow.Color = value;
        }

        /// <summary>
        /// Moves the glow above a cinematic veil (over the lit copy) or back among the units.
        /// </summary>
        public void GlowAboveVeil(bool on)
        {
            Color c = _glow.Color;
            _glow.Kill();
            _glow = on ? new TeamHalo(this, true, FxLayer.Cine, 7) : new TeamHalo(this, true, FxLayer.Over, 0);
            _glow.Color = c;
        }

        /// <summary>Draw the unit itself above a cinematic veil (it stays lit while the world dims).</summary>
        public bool Lit
        {
            get => _lit.Color.a > 0f;
            set => _lit.Color = value ? Color.white : new Color(1f, 1f, 1f, 0f);
        }

        /// <summary>Hands the unit back to the game; in mid-air it falls on its own.</summary>
        public void Release(bool onGround)
        {
            if (Finished)
            {
                return;
            }
            LandOnFinish = onGround;
            Blur = false;
            Finished = true;
            KillFx();
        }

        /// <summary>A white (or coloured) flash over the whole unit.</summary>
        public void Flash(float seconds, Color? color = null)
        {
            if (Finished)
            {
                return;
            }
            _flashColor = color ?? Color.white;
            _flashLeft = Mathf.Max(_flashLeft, seconds);
            if ((_flash == null || !_flash.Alive) && Fx.HasSilhouette)
            {
                Sprite s = Fx.UnitSprite(A);
                _flash = s != null ? Fx.Spawn(s, DrawPos(), 1f, 1f, FxLayer.Over, 2, true) : null;
                if (_flash != null)
                {
                    _flash.Held = true;
                    _flash.UseEuler = true;
                    _flash.Cine = Cinematic;
                    Fx.Glue(_flash, this, new Color(1f, 1f, 1f, 0f));
                }
            }
        }

        public override void Tick(float dt)
        {
            _flashLeft -= dt;
            // failsafe: a controller that ended (or vanished) without letting go; and a unit the
            // player possesses is handed straight back (the controller skips it as a lost one)
            if (Owner == null || Owner.Finished || T > 20f || ControllableUnit.isControllingUnit(A))
            {
                Release(Pose.Height <= 0.02f);
            }
        }

        public override void Frame()
        {
            _glow.Frame();
            _lit.Frame();
            if (_flash == null || !_flash.Alive)
            {
                return;
            }
            Fx.Glue(_flash, this, Fx.WithA(_flashColor, Mathf.Clamp01(_flashLeft / 0.05f)));
            if (_flashLeft <= -0.2f)
            {
                _flash.Kill();
                _flash = null;
            }
        }

        public override void Abort()
        {
            Blur = false;
            KillFx();
        }

        public override void OnEnd(bool aborted)
        {
            KillFx();
        }

        private void KillFx()
        {
            _glow.Kill();
            _lit.Kill();
            _flash?.Kill();
            _flash = null;
        }
    }

    /// <summary>
    /// A copy of a performing unit glued to it every frame: a flat-colour glow (silhouette) or the
    /// unit itself drawn above the cinematic veil. Spawned when it first becomes visible, killed
    /// with the move.
    /// </summary>
    internal sealed class TeamHalo
    {
        private readonly Move _m;
        private readonly bool _silhouette;
        private readonly FxLayer _layer;
        private readonly int _order;
        private Particle _p;

        public Color Color = new Color(1f, 1f, 1f, 0f);

        public TeamHalo(Move m, bool silhouette, FxLayer layer, int order)
        {
            _m = m;
            _silhouette = silhouette;
            _layer = layer;
            _order = order;
        }

        /// <summary>Call from Move.Frame (right before drawing).</summary>
        public void Frame()
        {
            if (Color.a <= 0.003f)
            {
                if (_p != null && _p.Alive)
                {
                    _p.R.enabled = false;
                }
                return;
            }
            if ((_p == null || !_p.Alive) && (!_silhouette || Fx.HasSilhouette))
            {
                Sprite s = Fx.UnitSprite(_m.A);
                _p = s != null ? Fx.Spawn(s, _m.DrawPos(), 1f, 1f, _layer, _order, _silhouette) : null;
                if (_p != null)
                {
                    _p.Held = true;
                    _p.UseEuler = true;
                    _p.Cine = true;
                }
            }
            Fx.Glue(_p, _m, Color);
        }

        public void Kill()
        {
            _p?.Kill();
            _p = null;
            Color.a = 0f;
        }
    }

    /// <summary>
    /// Shared rules for team techniques: who may join, who pays, the tempo by tier (a master never
    /// stands about), the hand-off to whoever follows up, the real damage to the world, and small
    /// drawing helpers.
    /// </summary>
    internal static class Crew
    {
        private static readonly Color Smoke = new Color(0.36f, 0.33f, 0.31f, 1f);
        private static readonly Dictionary<long, double> _failed = new Dictionary<long, double>();

        /// <summary>
        /// Remembers that a team technique could not be set up around this unit (no safe stations,
        /// no clear lines), so the planners don't try the same impossible thing every think.
        /// </summary>
        public static void Fail(string tech, Actor v, float seconds = 3f)
        {
            if (v == null)
            {
                return;
            }
            if (_failed.Count > 256)
            {
                _failed.Clear();
            }
            _failed[Key(tech, v)] = Fighter.Now + seconds;
        }

        public static bool Failed(string tech, Actor v)
        {
            return v != null && _failed.Count > 0 && _failed.TryGetValue(Key(tech, v), out double until) && Fighter.Now < until;
        }

        public static void ClearFailures()
        {
            _failed.Clear();
        }

        private static long Key(string tech, Actor v)
        {
            return v.Sid() * 31L + tech.GetHashCode();
        }

        /// <summary>
        /// An ally of 'leader' free to join a team technique right now: alive, same kingdom, on its
        /// feet (one in a light move, a flowing strike or a blur dash, is taken over, but never out
        /// of the air: it would snap down), not in a technique, not in another set piece or a duel,
        /// not a child, and not being steered by a battle situation (a champion, a challenger, an
        /// avenger, a rescuer has a story of its own to play out).
        /// </summary>
        public static bool Free(Actor leader, Actor u)
        {
            return u != null && u != leader && Combat.IsAlly(leader, u) && Moves.CanPerform(u) && Combat.HeightOf(u) <= 0.05f && !Director.InScene(u)
                && !u.isBaby() && !Duels.InDuel(u) && !Situations.Engaged(u);
        }

        /// <summary>
        /// May this fighter call a team technique? Not while it is in a duel: the duel is its fight,
        /// and a team technique on someone else would walk it out of it (a god-power order may).
        /// </summary>
        public static bool MayLead(Ctx c)
        {
            return c.Forced || !Duels.InDuel(c.Self);
        }

        /// <summary>What a partner pays for its share (tiered units only; awakened ones pay nothing).</summary>
        public static float Share(Actor u, float share)
        {
            if (Tiers.Of(u) == Tier.None || Fighters.Awakened(u))
            {
                return 0f;
            }
            return share * Settings.ManaCost;
        }

        public static bool CanPay(Actor u, float share)
        {
            float c = Share(u, share);
            return c <= 0f || u.getMana() >= c;
        }

        /// <summary>A partner pays its share and gets a short cooldown on the technique (if it is tiered).</summary>
        public static void Pay(Actor u, Technique t, float share, float cooldown)
        {
            int c = Mathf.CeilToInt(Share(u, share));
            if (c > 0)
            {
                u.spendMana(c);
            }
            if (Tiers.Of(u) != Tier.None)
            {
                Fighters.Get(u).SetCooldown(t, cooldown * Settings.Cooldowns);
            }
        }

        /// <summary>
        /// After a team technique: the partner catches its breath before its own next technique
        /// (its tier's gap). A Master or better doesn't stand about: it looks for its next move at
        /// once (the gap of a master is a blink).
        /// </summary>
        public static void Rest(Actor u)
        {
            if (u != null && Fighters.TryGet(u, out Fighter f))
            {
                f.LastEnd = Fighter.Now;
                if (f.Info != null && f.Info.Flow)
                {
                    f.ThinkIn = 0f;
                }
            }
        }

        /// <summary>
        /// Passes the combo on to a partner who is right there when the team technique ends (the
        /// finisher who just spiked the enemy, a cutter beside the enemy it popped up): a short link
        /// window on it, and it thinks at once, so the next technique follows without a break. Like
        /// the Brain's own chain windows, it only opens when the partner has a ready, affordable
        /// technique that takes the link (otherwise it just looks again at once). False if no
        /// window was opened.
        /// </summary>
        public static bool Handoff(Actor u, Sakuga.Link link, Actor target, float window = 0.45f)
        {
            if (u == null || !u.isAlive() || !Combat.Living(target) || link == Sakuga.Link.None || !Fighters.TryGet(u, out Fighter f))
            {
                return false;
            }
            f.ThinkIn = 0f;
            if (!CanTake(f, link))
            {
                return false;
            }
            f.Link = link;
            f.LinkTarget = target;
            f.LinkUntil = Fighter.Now + window;
            return true;
        }

        /// <summary>
        /// Does this fighter know a technique that takes 'link' and is ready and affordable right
        /// now (Brain.CanTake's rule, so a hand-off never opens a window nobody can use)?
        /// </summary>
        public static bool CanTake(Fighter f, Sakuga.Link link)
        {
            if (f == null || !f.Valid)
            {
                return false;
            }
            IReadOnlyList<Technique> all = Techniques.All;
            for (int i = 0; i < all.Count; i++)
            {
                Technique t = all[i];
                if (t.GodAbility || (t.Team && !Settings.TeamTechniques) || !t.Accepts(link))
                {
                    continue;
                }
                int level = Techniques.Level(f.A, t, f.Tier);
                if (level > 0 && f.Ready(t) && Mana.Has(f.A, t, level, true))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// What an ordinary blow is worth against Ctx.Expected's multiples (which include the tier's
        /// Might): below Master the plain swing (Ctx.Raw), from Master up a flowing strike (Might
        /// and all). For the "an ordinary blow ends it: no show needed" gates.
        /// </summary>
        public static float Plain(Ctx c)
        {
            if (c.Info != null && c.Info.Flow)
            {
                return 1f;
            }
            return Mathf.Clamp01(c.Raw / Mathf.Max(1f, c.D));
        }

        /// <summary>
        /// The end of a team technique for the one who led it. Below Master the game's own attack
        /// timer restarts (no ordinary swing glued to the end of a show); a Master or better fights
        /// with techniques and flowing strikes only, so nothing holds it back.
        /// </summary>
        public static void Breathe(Actor a)
        {
            if (a != null && a.isAlive() && !Flow(a))
            {
                a.startAttackCooldown();
            }
        }

        /// <summary>A Master or better: fights with techniques only, and never pauses between them.</summary>
        public static bool Flow(Actor a)
        {
            TierInfo ti = a != null ? Tiers.Info(Tiers.Of(a)) : null;
            return ti != null && ti.Flow;
        }

        /// <summary>
        /// How long the recover phases (landings, sheathing, getting up) last for this fighter: the
        /// tier's Linger (TierInfo), the same for every move in the mod: whole below Expert, then
        /// 0.85 (Expert), 0.55 (Master), 0.48 (Grandmaster), 0.42 (God): the higher, the crisper.
        /// </summary>
        public static float Pace(Actor a)
        {
            return a != null ? Mathf.Clamp(Tiers.Linger(a), 0.3f, 1f) : 1f;
        }

        /// <summary>
        /// The dramatic beats (a draw stance, the frozen heartbeat before a burst, a pass through the
        /// air) tighten less than the recoveries: 0.93 / 0.78 / 0.74 / 0.71 from Expert up.
        /// </summary>
        public static float Beat(Actor a)
        {
            return 1f - 0.5f * (1f - Pace(a));
        }

        /// <summary>Is a God among them (the world damage goes up a scale)?</summary>
        public static bool AnyGod(List<Actor> group)
        {
            if (group == null)
            {
                return false;
            }
            for (int i = 0; i < group.Count; i++)
            {
                if (group[i] != null && Tiers.Of(group[i]) == Tier.God)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The world damage of a team impact: 'force' (1 scorch, 2 crater, 3 burning crater, 4 God
        /// scale), one more (at most 4) and half again as wide when a God takes part.
        /// </summary>
        public static int Force(int force, bool god)
        {
            return force <= 0 ? 0 : god ? Mathf.Min(4, force + 1) : Mathf.Min(4, force);
        }

        public static float Radius(float radius, bool god)
        {
            return god ? radius * 1.5f : radius;
        }

        /// <summary>
        /// Real damage to the world (Kit.Ravage). The ground under a friend taking part is never set
        /// alight: if one of them stands where the blast would burn, the blast only craters (force 2).
        /// 'fire': false digs and blasts without any flames (a furrow whose banks are lit apart).
        /// </summary>
        public static void Ravage(Vector2 at, float radius, int force, Actor by, List<Actor> spare, bool fire = true)
        {
            if (force <= 0)
            {
                return;
            }
            if (fire && force >= 3 && Underfoot(at, radius, spare))
            {
                force = 2;
            }
            Kit.Ravage(at, radius, force, by, fire);
        }

        /// <summary>Masters and up fight through fire (their tier gives them the fire immunity).</summary>
        public static bool FireProof(Actor u)
        {
            return u != null && Tiers.Of(u) >= Tier.Master;
        }

        /// <summary>
        /// Sets the grass alight on the banks of a furrow: along the segment a..b, the tiles 'off'
        /// tiles to either side of it catch with 'chance' each (the trench itself is bare soil and
        /// stays dark, so it reads as a burning-edged scar, not a field of flames). Only with full
        /// environment damage; never on water or rock, never on the attacker's own kingdom's
        /// buildings, never within 1.5 tiles of a friend in 'spare' who can burn.
        /// </summary>
        public static void Banks(Vector2 a, Vector2 b, float off, float chance, Actor by, List<Actor> spare)
        {
            if (chance <= 0f || Settings.Destruction < 2 || World.world == null)
            {
                return;
            }
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f)
            {
                return;
            }
            Vector2 dir = d / len;
            Vector2 side = new Vector2(-dir.y, dir.x);
            Kingdom own = by != null ? by.kingdom : null;
            int n = Mathf.Max(1, Mathf.RoundToInt(len));
            try
            {
                for (int i = 0; i < n; i++)
                {
                    Vector2 mid = a + dir * ((i + 0.5f) * len / n);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        if (Random.value >= chance)
                        {
                            continue;
                        }
                        Vector2 p = Combat.Clamp(mid + side * s * (off + Random.Range(-0.3f, 0.3f)));
                        if (NearSpare(p, 1.5f, spare))
                        {
                            continue;
                        }
                        WorldTile t = Combat.TileAt(p);
                        if (t == null || t.Type == null || t.Type.liquid || t.Type.block || t.Type.lava || t.isOnFire())
                        {
                            continue;
                        }
                        if (own != null && t.hasBuilding() && t.building != null && t.building.kingdom == own)
                        {
                            continue;
                        }
                        t.startFire(true);
                    }
                }
            }
            catch (System.Exception e)
            {
                SakugaMod.Report("team banks", e);
            }
        }

        /// <summary>A friend in 'spare' who can burn stands within r tiles of p.</summary>
        private static bool NearSpare(Vector2 p, float r, List<Actor> spare)
        {
            if (spare == null)
            {
                return false;
            }
            float r2 = r * r;
            for (int i = 0; i < spare.Count; i++)
            {
                Actor u = spare[i];
                if (u != null && u.isAlive() && !FireProof(u) && (u.current_position - p).sqrMagnitude <= r2)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A crater with a burning heart: the wide blast, and (when a friend who can burn stands
        /// inside it, so the wide blast may only crater) the crater still dug as deep, its fire kept
        /// to the very spot the blow fell on, and none at all with that friend right beside it.
        /// </summary>
        public static void Blast(Vector2 at, float radius, int force, Actor by, List<Actor> spare)
        {
            if (force <= 0)
            {
                return;
            }
            if (force >= 3 && radius >= 1.5f && Underfoot(at, radius, spare))
            {
                Kit.Ravage(at, radius, 2, by);
                Kit.Ravage(at, 1f, force, by, !NearSpare(at, 2.2f, spare));
                return;
            }
            Ravage(at, radius, force, by, spare);
        }

        /// <summary>
        /// Does one of 'spare' who can burn stand where a blast of this radius may set the ground
        /// alight (any tile of its brush, its rim included: Kit.Ravage's own radius and rounding)?
        /// Masters and up don't count: they fight on through the flames.
        /// </summary>
        private static bool Underfoot(Vector2 at, float radius, List<Actor> spare)
        {
            if (spare == null || spare.Count == 0)
            {
                return false;
            }
            WorldTile c = Combat.TileAt(at);
            if (c == null)
            {
                return false;
            }
            int r = Kit.BrushRadius(radius);
            if (r <= 0)
            {
                return false;   // a single tile: the blast digs it, never burns it
            }
            int r2 = r * r;
            for (int i = 0; i < spare.Count; i++)
            {
                Actor u = spare[i];
                if (u == null || !u.isAlive() || FireProof(u))
                {
                    continue;
                }
                WorldTile t = Combat.TileAt(u.current_position);
                if (t == null)
                {
                    continue;
                }
                int dx = t.x - c.x, dy = t.y - c.y;
                if (dx * dx + dy * dy <= r2)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The unit's size against a human (Move.K for a unit that isn't performing).</summary>
        public static float KOf(Actor a)
        {
            return Mathf.Sqrt(Mathf.Clamp(a.actor_scale / 0.1f, 0.6f, 4f));
        }

        /// <summary>World units per sprite pixel of a unit (Move.Px).</summary>
        public static float PxOf(Actor a)
        {
            return Mathf.Clamp(a.actor_scale, 0.04f, 0.4f);
        }

        /// <summary>Sorting key: tiered before untiered, higher tiers first.</summary>
        public static int Rank(Actor a)
        {
            return (int)Tiers.Of(a);
        }

        /// <summary>The flash a hard-hit victim gives: its own tier colour if it has one.</summary>
        public static Color VictimFlash(Actor v)
        {
            Tier t = Tiers.Of(v);
            return t == Tier.None ? Color.white : Color.Lerp(Tiers.LightColor(t), Color.white, 0.35f);
        }

        /// <summary>Faces 'to' from 'from' (keeps 'current' when it is straight above or below).</summary>
        public static bool Face(Vector2 from, Vector2 to, bool current)
        {
            float dx = to.x - from.x;
            return Mathf.Abs(dx) > 0.06f ? dx > 0f : current;
        }

        public static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        public static Vector2 Dir(float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }

        /// <summary>A safe spot near 'want' (it, or within a small spiral), else 'fallback'.</summary>
        public static Vector2 SafeNear(Vector2 want, Vector2 fallback, float radius = 1f)
        {
            if (Combat.SafeSpot(want))
            {
                return want;
            }
            return Combat.FindLanding(want, radius, out Vector2 spot) ? spot : fallback;
        }

        /// <summary>
        /// Is this a sensible target for a team technique (a foe, not in a set piece, not someone's
        /// duel, not in a technique)? One in a light move (a flowing strike, a blur dash) is fair
        /// game: the team takes it over (Moves.Locked).
        /// </summary>
        public static bool Target(Actor self, Actor v)
        {
            return v != null && Combat.IsFoe(self, v) && !Director.InScene(v) && !Moves.Locked(v) && !Duels.InDuel(v);
        }

        /// <summary>
        /// The team takes the enemy over: a light move it is in (a flowing strike, a blur dash) is
        /// cut short at once, so it stands where the team closes in instead of dashing out of it.
        /// </summary>
        public static void Seize(Actor v)
        {
            if (v != null && v.isAlive() && Moves.Busy(v) && !Moves.Locked(v))
            {
                Moves.Interrupt(v);
            }
        }

        /// <summary>
        /// The enemy a team is closing in on doesn't act for 'seconds': no attack, and so none of the
        /// game's combat hops that come with one (an archer's backstep would carry it out of reach
        /// and into the air right as the first blow lands). It still stands and can still answer
        /// the blow itself (Still Water, Mirage Step: Combat.Reach).
        /// </summary>
        public static void Pin(Actor v, float seconds)
        {
            Combat.Pin(v, seconds);
        }

        /// <summary>
        /// Combat.CanCatch: Combat.CanPuppet, except that a unit caught low in a hop (a backstep, a
        /// dodge, a shove; never a flier) can still be grabbed out of the air. 'height' is where it
        /// is caught.
        /// </summary>
        public static bool Grabbable(Actor v, Actor by, out float height)
        {
            height = 0f;
            if (!Combat.CanCatch(v, by))
            {
                return false;
            }
            height = Mathf.Max(0f, v.position_height);
            return true;
        }

        /// <summary>A puff of smoke where something fizzles out.</summary>
        public static void Fizzle(Vector2 at, float px, bool cine)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            int n = Fx.Count(4);
            for (int i = 0; i < n; i++)
            {
                Particle p = Fx.Spawn(Art.Dust[Random.Range(1, 4)], at + Random.insideUnitCircle * 2f * px, px, Random.Range(0.4f, 0.6f), FxLayer.Top, 5);
                if (p == null)
                {
                    break;
                }
                p.Moving(Random.insideUnitCircle * 6f * px, 10f * px, 2f).Sized(0.5f, 1.1f)
                    .Color(Fx.WithA(Smoke, 0.5f), Fx.WithA(Smoke, 0.3f), Fx.WithA(Smoke, 0f));
                p.Cine = cine;
            }
        }

        /// <summary>A flickering line in a unit's tier colour between two units (a hand-off, a link).</summary>
        public static void Link(Vector2 a, Vector2 b, float px, Color c, float life = 0.18f, bool cine = false)
        {
            if (!Kit.Visible(Vector2.Lerp(a, b, 0.5f)))
            {
                return;
            }
            Particle p = Kit.Line(a, b, px, life, 1, FxLayer.Top, 3, Fx.WithA(Color.white, 0.9f), Fx.WithA(c, 0.8f), Fx.WithA(c, 0f));
            if (p != null)
            {
                p.Cine = cine;
            }
        }

        /// <summary>Distinct tier colours of a group (a braid needs at least two strands).</summary>
        public static List<Color> Braid(List<Actor> group)
        {
            List<Color> list = new List<Color>();
            List<Tier> seen = new List<Tier>();
            foreach (Actor u in group)
            {
                Tier t = Tiers.Of(u);
                if (seen.Contains(t))
                {
                    continue;
                }
                seen.Add(t);
                list.Add(t == Tier.None ? new Color(1f, 0.9f, 0.7f, 1f) : Tiers.Color(t));
                if (list.Count >= 4)
                {
                    break;
                }
            }
            if (list.Count == 0)
            {
                list.Add(Color.white);
            }
            if (list.Count == 1)
            {
                Tier t = seen.Count > 0 ? seen[0] : Tier.None;
                list.Add(Color.Lerp(t == Tier.None ? Color.white : Tiers.LightColor(t), Color.white, 0.2f));
            }
            return list;
        }
    }
}
