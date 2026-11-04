using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// WORLD'S END: two Primordials who are not of one kingdom cannot share a world. The moment there
    /// are two, they find each other wherever they stand, and their battle ends the world. Nothing
    /// survives it but the one who wins: every unit, every building, every forest.
    ///
    ///   OMEN        the sky goes black and red, the whole world quakes, the sky splits with lightning;
    ///               both Primordials rise into the air, blazing
    ///   CONVERGENCE they tear through space (a void opening where each stood) and meet in the sky
    ///               between where they were; the first clash levels everything for forty tiles
    ///   THE BATTLE  they fight all over the world like the end of an anime: hyper rushes that fling the
    ///               other across the land, a thousand collisions in a breath (a crater at each), a duel
    ///               high in the sky that ends with one spiked into the earth, a throw that ploughs a
    ///               river of lava across a continent, beams pushing against each other, orb barrages,
    ///               comet dives from out of sight, a chase low over the land. Wherever either moves the
    ///               world is torn: low, a trench and nothing left alive in it; high, sonic booms that
    ///               flatten what is under them. Every two bouts, one of them throws a catastrophe:
    ///               in turn, each throws a power that is a catastrophe for the planet (each once):
    ///                 Planet Cleaver   one cut through the other and on across the whole world: the
    ///                                  land opens along it from edge to edge, a river of lava in it
    ///                 Starfall Apocalypse  meteors fall on the whole world, hundreds of them
    ///                 Continental Collapse the land round the other breaks and sinks into the sea
    ///                 Singularity      a black star swallows everything round the other, collapses
    ///                                  and goes off like a dying sun
    ///                 Sunfall          a second sun is dragged down out of the sky onto the other
    ///                 World Fire       the first fire: the whole world bursts into flame
    ///               and between them they crash together, each clash a crater forty tiles across
    ///   THE END     both gather everything they are; two beams meet between them and push; one gives
    ///               way, and the loser is unmade. The meeting point bursts, and the wave of it runs out
    ///               across the whole world: every unit dies, every building and tree falls, the land
    ///               itself dies (grey wasteland, as a nuke leaves it), craters are torn in it and rivers
    ///               of lava split it from where it began to the ends of the world.
    ///               The catastrophes grow with the world (WorldScale: twice as big on a titanic map).
    ///               The winner comes down alone into a dead world.
    /// The "World's End" setting (on by default) can forbid it: two Primordials then simply fight as Gods.
    /// </summary>
    internal sealed partial class WorldsEnd
    {
        public static WorldsEnd Current;

        /// <summary>Dev: bouts or catastrophes the next world's end plays first.</summary>
        public static List<string> ForceFirst;
        private static float _look;

        public static void Clear()
        {
            Current?.Stop("cleared");
            Current = null;
        }

        /// <summary>Is 'a' one of the two in the battle (nothing else may move it)?</summary>
        public static bool Holds(Actor a)
        {
            return Current != null && a != null && (a.Sid() == Current._idA || a.Sid() == Current._idB);
        }

        /// <summary>Is the world ending right now?</summary>
        public static bool Active => Current != null;

        public static void Tick(float dt)
        {
            if (!Settings.WorldsEnd)
            {
                Clear();
                return;
            }
            if (Current != null)
            {
                try
                {
                    if (!Current.Step(dt))
                    {
                        Current = null;
                    }
                }
                catch (System.Exception e)
                {
                    SakugaMod.Report("world's end", e);
                    Current.Stop("error");
                    Current = null;
                }
                return;
            }
            _look -= dt;
            if (_look > 0f)
            {
                return;
            }
            _look = 0.25f;
            if (!Settings.WorldsEnd)
            {
                return;
            }
            IReadOnlyList<Actor> l = Primordials.Living;
            for (int i = 0; i < l.Count; i++)
            {
                for (int j = i + 1; j < l.Count; j++)
                {
                    Actor a = l[i], b = l[j];
                    if (a == null || b == null || !a.isAlive() || !b.isAlive() || Awakenings.Waiting(a) || Awakenings.Waiting(b))
                    {
                        continue;   // (a Primordial still awakening: the end of the world waits for it)
                    }
                    // (one kingdom's two Primordials stand together; any other two cannot share the world)
                    if (a.kingdom != null && a.kingdom == b.kingdom)
                    {
                        continue;
                    }
                    WorldsEnd w = new WorldsEnd(a, b);
                    if (w.Begin())
                    {
                        Current = w;
                        return;
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------- holding --

        /// <summary>A Primordial held by the battle: its pose is set every frame; nothing else can touch it.</summary>
        internal sealed class Hold : Move
        {
            public float Idle;

            public Hold(Actor a) : base(a)
            {
                Level = 3;
            }

            public override bool SuperArmor => true;

            public override bool Intangible => true;

            public override bool Cinematic => true;

            public override void Tick(float dt)
            {
                Idle += dt;
                // (it lets go once the battle stops driving it; while the battle still runs, only after a
                // long wait: another cinematic freezing the world stops the battle's steps but not this
                // cinematic move's time, and both holds used to lapse mid-battle)
                if (!Finished && Idle > 1f && (!Active || Idle > 30f))
                {
                    Let();
                }
            }

            public void Let()
            {
                if (Finished)
                {
                    return;
                }
                Pose.Rot = 0f;
                Pose.SX = Pose.SY = 1f;
                Pose.Nudge = Vector2.zero;
                Pose.Hidden = false;
                LandOnFinish = true;
                Pose.Height = 0f;
                Pose.HideShadow = false;
                Finished = true;
            }
        }

        // -------------------------------------------------------------------------------- state --

        private enum Phase
        {
            Omen,
            Converge,
            Clash,
            Power,
            Final,
            Extinction,
            After
        }

        private readonly Actor _a, _b;
        private readonly long _idA, _idB;
        private Hold _ha, _hb;
        private Vector2 _center, _axis;
        private Phase _ph = Phase.Omen;
        private float _t, _pt, _fxT, _shakeT;
        private readonly List<string> _powers = new List<string>();
        private int _power;
        private bool _aAttacks;
        private bool _aWins;
        private bool _entered;
        private readonly float _k, _px;
        private readonly Queue<Actor> _doom = new Queue<Actor>();
        private readonly HashSet<long> _doomed = new HashSet<long>();
        private readonly List<Actor> _scan = new List<Actor>();
        private Vector2 _a0, _b0;
        private float _sep = 4.5f;
        // the world's end wave
        private float _waveR, _waveDone;
        private Vector2 _waveAt;
        private int _fires;
        // a power's own state
        private readonly List<Vector3> _falls = new List<Vector3>();
        private float _cutDone;
        private Vector2 _cutA, _cutB, _sunAt;
        private float _sinkDone;
        private int _spawned;

        private static void Shuffle(List<string> l)
        {
            for (int i = l.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                string t = l[i];
                l[i] = l[j];
                l[j] = t;
            }
        }

        private static bool Ender(string p)
        {
            return p == "cleaver" || p == "starfall" || p == "collapse" || p == "singularity" || p == "sunfall" || p == "fire"
                || p == "tide" || p == "frost" || p == "shatter";
        }

        private WorldsEnd(Actor a, Actor b)
        {
            _a = a;
            _b = b;
            _idA = a.Sid();
            _idB = b.Sid();
            _k = Mathf.Max(Choreo.KOf(a), Choreo.KOf(b));
            _px = Mathf.Min(Choreo.PxOf(a), Choreo.PxOf(b));
        }

        private bool Alive(Actor x, long id)
        {
            return x != null && x.Sid() == id && x.isAlive();
        }

        /// <summary>The world's size against a standard map (1; a titanic map is 2): the catastrophes grow with it.</summary>
        private static float WorldScale => Mathf.Clamp(Mathf.Sqrt(MapBox.width * (float)MapBox.height / 65536f), 1f, 3f);

        /// <summary>A blast radius grown with the world (never past what the game can take in one go).</summary>
        private static int Big(float r)
        {
            return Mathf.RoundToInt(Mathf.Min(r * WorldScale, 100f));
        }

        private Actor Attacker => _aAttacks ? _a : _b;

        private Actor Defender => _aAttacks ? _b : _a;

        private Hold HAtk => _aAttacks ? _ha : _hb;

        private Hold HDef => _aAttacks ? _hb : _ha;

        private bool Begin()
        {
            if (!Alive(_a, _idA) || !Alive(_b, _idB))
            {
                return false;
            }
            // nothing either was doing matters any more (a technique, a duel, a blow taken)
            if (Moves.TryGet(_a, out Move ma))
            {
                Moves.Cancel(ma);
            }
            if (Moves.TryGet(_b, out Move mb))
            {
                Moves.Cancel(mb);
            }
            _ha = new Hold(_a);
            _hb = new Hold(_b);
            if (!Moves.Start(_ha))
            {
                return false;
            }
            if (!Moves.Start(_hb))
            {
                _ha.Let();
                return false;
            }
            _a0 = _a.current_position;
            _b0 = _b.current_position;
            _center = Combat.Clamp((_a0 + _b0) * 0.5f);
            Vector2 d = _b0 - _a0;
            _axis = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right;
            if (Mathf.Abs(_axis.x) < 0.3f)
            {
                _axis = new Vector2(_axis.x >= 0f ? 1f : -1f, 0f);
            }
            _ha.Pose.Ground = _a0;
            _hb.Pose.Ground = _b0;
            // who will stand at the end: the stronger, as they are now (an upset now and then)
            float pa = Combat.HpPct(_a) * Random.Range(0.7f, 1.3f), pb = Combat.HpPct(_b) * Random.Range(0.7f, 1.3f);
            _aWins = pa >= pb;
            _aAttacks = Random.value < 0.5f;
            List<string> enders = new List<string> { "cleaver", "starfall", "collapse", "singularity", "sunfall", "fire", "tide", "frost", "shatter" };
            Shuffle(enders);
            enders.RemoveRange(7, enders.Count - 7);
            List<string> anime = new List<string> { "rush", "collisions", "skyduel", "throw", "beams", "orbs", "comet", "chase",
                "blades", "charge", "instant", "gravity", "mountain", "serpents", "echoes", "tear" };
            Shuffle(anime);
            // the first meeting is always a clash; then two bouts, a catastrophe, two bouts, a catastrophe...
            _powers.Add("clash");
            int ai = 0;
            foreach (string e in enders)
            {
                for (int i = 0; i < 2; i++)
                {
                    _powers.Add(anime[ai++ % anime.Count]);
                }
                _powers.Add(e);
            }
            if (ForceFirst != null && ForceFirst.Count > 0)
            {
                _powers.InsertRange(1, ForceFirst);   // (dev: these first)
                ForceFirst = null;
            }
            if (Dev.On) Dev.Trace("world's end: " + _a.asset.id + "#" + _idA + " vs " + _b.asset.id + "#" + _idB + " at " + _center + " (" + string.Join(", ", _powers) + ")");
            Choreo.Sound("event:/SFX/NATURE/EarthQuake", Combat.TileAt(_center));
            Spotlight.WorldsEnd(_a, _b);   // (logged; the camera goes there with the Spotlight on)
            return true;
        }

        /// <summary>When the last world's end ended (unscaled time).</summary>
        public static float LastEnded = -999f;

        public void Stop(string why)
        {
            LastEnded = Time.unscaledTime;
            _ha?.Let();
            _hb?.Let();
            Cinema.End(this);
            if (Dev.On) Dev.Trace("world's end over: " + why);
        }

        private void Next(Phase p)
        {
            _ph = p;
            _pt = 0f;
            _entered = false;
            _enter2 = false;
        }

        private bool Enter()
        {
            if (_entered)
            {
                return false;
            }
            _entered = true;
            return true;
        }

        // ---------------------------------------------------------------------------------- step --

        private bool Step(float dt)
        {
            bool la = Alive(_a, _idA), lb = Alive(_b, _idB);
            if (_ph < Phase.Extinction && (!la || !lb))
            {
                // one fell some other way: the survivor's power runs out over the world all the same
                _aWins = la;
                Next(Phase.Extinction);
                // the wave runs out from where the fallen one fell (its body while it is still that unit,
                // else where its hold last had it), not from where the battle began: they cross the map
                Actor dead = la ? _b : _a;
                Hold dh = la ? _hb : _ha;
                _waveAt = dead != null && dead.Sid() == (la ? _idB : _idA) ? dead.current_position
                    : dh != null ? dh.Pose.Ground : (la ? _b0 : _a0);
                _waveR = _waveDone = 0f;
            }
            if (_ph < Phase.Final)
            {
                _ha = Rehold(_ha, _a);
                _hb = Rehold(_hb, _b);
            }
            _t += dt;
            _pt += dt;
            if (_ha != null)
            {
                _ha.Idle = 0f;
            }
            if (_hb != null)
            {
                _hb.Idle = 0f;
            }
            if (Cinema.TryBegin(this, 0f) && _ph < Phase.After)
            {
                Cinema.Dim(_ph == Phase.Extinction ? 0.15f : 0.42f, new Color(0.18f, 0.02f, 0.05f, 1f), 1.2f, true);
            }
            if (_ph < Phase.Final)
            {
                Floor(_a);
                Floor(_b);
            }
            Doom(dt);
            Storm(dt);
            WorldStorm(dt);
            Scars();
            if (Dev.On && _ha != null && _hb != null)
            {
                // (test captures follow the fight round the world)
                Hold w = _aWins ? _ha : _hb;
                Dev.Watch(_ph >= Phase.Extinction ? Body(w) : (Body(_ha) + Body(_hb)) * 0.5f);
            }
            switch (_ph)
            {
                case Phase.Omen: Omen(); break;
                case Phase.Converge: Converge(); break;
                case Phase.Clash: Clash(); break;
                case Phase.Power: Power(dt); break;
                case Phase.Final: Final(); break;
                case Phase.Extinction: Extinction(dt); break;
                default: return After();
            }
            return true;
        }

        // ------------------------------------------------------------------------------ helpers --

        private Vector2 PosA => _center - _axis * _sep * 0.5f * _k;

        private Vector2 PosB => _center + _axis * _sep * 0.5f * _k;

        private float Air => (3f + 0.25f * Mathf.Sin(_t * 2.2f)) * _k;

        private void Hover(Hold h, Vector2 at, float height, Vector2 face)
        {
            if (h == null || h.Finished)
            {
                return;
            }
            h.Pose.Ground = Vector2.Lerp(h.Pose.Ground, at, 0.18f);
            h.Pose.Height = Mathf.Lerp(h.Pose.Height, height, 0.12f);
            h.Pose.HideShadow = h.Pose.Height > 0.05f;
            float dx = face.x - h.Pose.Ground.x;
            if (Mathf.Abs(dx) > 0.05f)
            {
                h.Pose.FaceRight = dx > 0f;
            }
        }

        private void HoverBoth(float height)
        {
            Hover(_ha, PosA, height, PosB);
            Hover(_hb, PosB, height, PosA);
        }

        private static Vector2 Body(Hold h)
        {
            return h != null ? h.Pose.Ground + new Vector2(0f, h.Pose.Height + Choreo.SpritePx(h.A) * h.Px * 0.5f) : Vector2.zero;
        }

        private bool Vis(Vector2 at, float margin = 0.8f)
        {
            return Kit.Visible(at, margin);
        }

        /// <summary>Everyone within 'r' tiles of 'at' (but the two) is doomed: they die over the next frames.</summary>
        private void DoomAround(Vector2 at, float r)
        {
            Primordials.Around(null, at, r, true, _scan);
            foreach (Actor u in _scan)
            {
                long id = u.Sid();
                if (id == _idA || id == _idB || _doomed.Contains(id))
                {
                    continue;
                }
                _doomed.Add(id);
                _doom.Enqueue(u);
            }
        }

        private void DoomOne(Actor u)
        {
            if (u == null || !u.isAlive())
            {
                return;
            }
            long id = u.Sid();
            if (id == _idA || id == _idB || _doomed.Contains(id))
            {
                return;
            }
            _doomed.Add(id);
            _doom.Enqueue(u);
        }

        /// <summary>The doomed die, some each frame (a world of deaths at once would stall the game).</summary>
        private void Doom(float dt)
        {
            int n = 0;
            while (_doom.Count > 0 && n < 120)
            {
                Actor u = _doom.Dequeue();
                n++;
                if (u == null || !u.isAlive())
                {
                    continue;
                }
                Actor by = _aWins ? _a : _b;
                try
                {
                    u.getHit(u.getMaxHealth() * 50f + 100000f, true, AttackType.Other, Alive(by, by == _a ? _idA : _idB) ? by : null, false, false, true);
                }
                catch (System.Exception)
                {
                }
                if (u.isAlive())
                {
                    u.dieAndDestroy(AttackType.Other);   // nothing is spared, not even a God's last light
                }
            }
        }

        /// <summary>A blast of the world itself: the game's own bomb terraform (craters, fire, buildings), everyone in it doomed.</summary>
        private void Blast(Vector2 at, int r, bool czar, Actor by)
        {
            WorldTile t = Combat.TileAt(Combat.Clamp(at));
            if (t != null && Settings.Destruction > 0)
            {
                MapAction.damageWorld(t, r, czar ? TerraformLibrary.czar_bomba : TerraformLibrary.atomic_bomb, by);
            }
            DoomAround(at, r * 0.9f);
        }

        /// <summary>A white-hot burst with prismatic rings: the heart of every great impact.</summary>
        private void Burst(Vector2 at, float tiles, bool flash)
        {
            if (Vis(at, 1f))
            {
                DuelFx.Starburst(at, _px, tiles * 0.45f, Color.white, Primordials.Prism(), Primordials.Prism(0.5f), 0.18f);
                Spec.Fireball(at, tiles * 0.35f, _px, Color.white, Color.Lerp(Primordials.Prism(), Color.white, 0.3f), Primordials.Prism(0.4f), false);
                Fx.Ring(at, _px, 0.7f, 2f, tiles / _px * 1.3f, 3, 1, FxLayer.Top, 22, Art.Ground, 2.6f, Color.white, Fx.WithA(Primordials.Prism(), 0.7f), Fx.WithA(Primordials.Void, 0f));
                Fx.Ring(at, _px, 1.1f, 2f, tiles / _px * 2.2f, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.2f, Fx.WithA(Primordials.Void, 0.7f), Fx.WithA(Primordials.Prism(0.5f), 0.4f), Fx.WithA(Primordials.Void, 0f));
                Spec.Rocks(at, _px, 30, 140f, _k);
                Kit.Shake(at, 0.6f, 0.02f);
            }
            if (flash)
            {
                Primordials.Flash(Fx.WithA(Color.white, 0.75f), 0.35f);
            }
            World.world.startShake(0.8f, 0.01f, 3f);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", Combat.TileAt(Combat.Clamp(at)));
        }

        /// <summary>A beam of the first light: a black rim, prismatic edges, a white core.</summary>
        private void Beam(Vector2 from, Vector2 to, int thick, float life)
        {
            PrimFx.Beam(from, to, (thick + 2) * _px * 1.5f, life, FxLayer.Top, 21, false, Primordials.Prism(), Primordials.Prism(0.4f));
        }

        /// <summary>The sky over the battle never rests: lightning, rocks torn loose, the ground cracking.</summary>
        private void Storm(float dt)
        {
            _fxT -= dt;
            if (_fxT > 0f || _ph >= Phase.After)
            {
                return;
            }
            _fxT = 0.12f;
            Vector2 at = Combat.Clamp(_center + Random.insideUnitCircle * 22f * _k);
            if (Random.value < 0.45f && Vis(at, 1f))
            {
                Vector2 sky = at + new Vector2(Random.Range(-3f, 3f), 22f) * _k;
                PrimFx.Bolt(sky, at, 3f * _px, 0.2f, Random.value < 0.5f ? new Color(1f, 0.25f, 0.2f, 1f) : Primordials.Prism(), FxLayer.Top, 24, false);
                Kit.Flash(at, _px, 6, 0.08f, Color.white);
                Kit.Ravage(at, 1f, 1, null, false);
            }
            if (Random.value < 0.6f && Vis(at, 1f))
            {
                Color soil = Fx.SoilColor(Combat.TileAt(at));
                Particle c = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], at, _px * Random.Range(1.2f, 2.6f), Random.Range(1.2f, 2f), FxLayer.Top, 9);
                if (c != null)
                {
                    c.Moving(new Vector2(Random.Range(-4f, 4f), Random.Range(20f, 45f)) * _px * _k, 0f, 1.2f, 20f * _px);
                    c.Spin = Random.Range(-200f, 200f);
                    c.Color(Color.Lerp(soil, Color.white, 0.15f), soil, Fx.WithA(soil, 0f));
                }
            }
            if (Random.value < 0.25f)
            {
                Vector2 dir = Random.insideUnitCircle.normalized;
                Vector2 to = Combat.Clamp(at + dir * Random.Range(4f, 12f) * _k);
                if (Vis(at, 1f))
                {
                    PrimFx.Crack(at, (to - at).normalized, (to - at).magnitude, 2.2f * _px, 1.3f, new Color(1f, 0.35f, 0.15f, 1f), FxLayer.Ground, 3, false, true, 1, 0f, 6f);
                }
                if (Settings.Destruction > 0)
                {
                    Kit.Trench(at, to, 1f, 2, null, false);
                }
            }
            _shakeT -= 0.12f;
            if (_shakeT <= 0f)
            {
                _shakeT = 1.5f;
                World.world.startShake(1.4f, 0.02f, 1.2f);
            }
        }

        // -------------------------------------------------------------------------------- omen --

        private void Omen()
        {
            if (Enter())
            {
                Cinema.Title("WORLD'S END", new Color(1f, 0.25f, 0.2f, 1f), 3.2f, _center + new Vector2(0f, 6f * _k), 1.4f);
                Primordials.Flash(Fx.WithA(new Color(1f, 0.2f, 0.1f, 1f), 0.5f), 0.6f);
                World.world.startShake(3f, 0.01f, 2.5f);
            }
            // both rise where they stand, blazing; the sky splits
            float k = Mathf.Clamp01(_pt / 3.5f);
            Hover(_ha, _a0, Air * k, _b0);
            Hover(_hb, _b0, Air * k, _a0);
            foreach (Hold h in new[] { _ha, _hb })
            {
                if (Random.value < 0.6f && Vis(Body(h), 0.5f))
                {
                    Trails.Ghost(h.A, 0.2f, Fx.WithA(Primordials.Prism(Random.value), 0.7f), Fx.WithA(Primordials.Void, 0f));
                    DuelFx.Rising(Body(h), _px, 3, 1.5f * _k, Primordials.Prism(Random.value));
                }
            }
            if (Random.value < 0.25f)
            {
                // the sky cracks open: a jagged seam of red light high over the world
                Vector2 c = Fx.OnScreen(_center, 0f) ? _center : _a0;
                Vector2 p0 = c + new Vector2(Random.Range(-14f, 14f), Random.Range(10f, 16f)) * _k;
                PrimFx.Bolt(p0, p0 + new Vector2(Random.Range(-8f, 8f), Random.Range(-3f, 3f)) * _k, 3f * _px, 0.4f, new Color(1f, 0.2f, 0.15f, 1f), FxLayer.Top, 24, false);
            }
            if (_pt >= 3f)
            {
                Next(Phase.Converge);
            }
        }

        // ---------------------------------------------------------------------------- converge --

        private void Converge()
        {
            if (Enter())
            {
                // each tears through space: a void opens where it stood and it is gone
                foreach (Hold h in new[] { _ha, _hb })
                {
                    Vector2 at = Body(h);
                    if (Vis(at, 1f))
                    {
                        Spec.Blob(at, 1.4f * _k, _px, 0.5f, FxLayer.Top, 24, 1f, 0.3f, Fx.WithA(Primordials.Void, 0.95f), Fx.WithA(Primordials.Prism(), 0.6f), Fx.WithA(Primordials.Void, 0f))?.Sized(1f, 0.1f);
                        Fx.Ring(at, _px, 0.4f, 20f * _k, 1f, 2, 1, FxLayer.Top, 23, 1f, 2f, Fx.WithA(Primordials.Prism(), 0f), Fx.WithA(Color.white, 0.9f));
                    }
                    h.Pose.Hidden = true;
                }
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", Combat.TileAt(_center));
            }
            if (_pt >= 0.45f && _ha.Pose.Hidden)
            {
                // and they are face to face in the sky between where they were
                foreach (Hold h in new[] { _ha, _hb })
                {
                    h.Pose.Hidden = false;
                    h.Pose.Ground = h == _ha ? PosA : PosB;
                    h.Pose.Height = Air;
                    if (Vis(Body(h), 1f))
                    {
                        Spec.Blob(Body(h), 1.2f * _k, _px, 0.4f, FxLayer.Top, 24, 1f, 0.3f, Color.white, Fx.WithA(Primordials.Prism(), 0.7f), Fx.WithA(Primordials.Void, 0f))?.Sized(0.2f, 1.2f);
                    }
                }
            }
            if (!_ha.Pose.Hidden)
            {
                HoverBoth(Air);
            }
            if (_pt >= 1.05f)
            {
                Next(Phase.Power);
            }
        }

        // ------------------------------------------------------------------------------- clash --

        private void Clash()
        {
            Next(Phase.Power);
        }

        private bool ClashBout()
        {
            // they crash together in the middle of the sky; the land below is levelled
            float k = Mathf.Clamp01(_pt / 0.35f);
            if (_pt < 0.35f)
            {
                _ha.Pose.Ground = Vector2.Lerp(PosA, _center - _axis * 0.35f * _k, Anim.InQuad(k));
                _hb.Pose.Ground = Vector2.Lerp(PosB, _center + _axis * 0.35f * _k, Anim.InQuad(k));
                _ha.Pose.Rot = _hb.Pose.Rot = 25f;
                Trails.Ghost(_a, 0.15f, Fx.WithA(Primordials.Prism(), 0.7f), Fx.WithA(Primordials.Void, 0f));
                Trails.Ghost(_b, 0.15f, Fx.WithA(Primordials.Prism(0.5f), 0.7f), Fx.WithA(Primordials.Void, 0f));
            }
            else if (Enter2())
            {
                Vector2 at = (Body(_ha) + Body(_hb)) * 0.5f;
                Burst(at, 12f * _k, true);
                Blast(_center, Big(_power == 0 ? 40 : 28), _power == 0, null);
                Kit.Stop(at, 0.2f);
                Strike(_a, _b, 0.03f);
                Strike(_b, _a, 0.03f);
                _ha.Pose.Rot = _hb.Pose.Rot = 0f;
            }
            else
            {
                HoverBoth(Air);
            }
            return _pt >= 0.85f;
        }

        // ------------------------------------------------------------------------------- power --

        private void Power(float dt)
        {
            string p = _powers[_power];
            if (Enter())
            {
                _falls.Clear();
                _cutDone = 0f;
                _sinkDone = 0f;
                _spawned = 0;
                _hits = 0;
                _bp = 0;
                _bt = 0f;
                ResetMore();
                // the one who will win attacks a little more often as the battle goes on
                if (p != "clash" && _power > 1)
                {
                    _aAttacks = Random.value < (_aWins ? 0.6f : 0.4f);
                }
                if (p != "clash" && !Land(_center))
                {
                    Relocate();
                }
                if (Dev.On) Dev.Trace("world's end: " + Attacker.asset.id + "#" + Attacker.Sid() + " - " + p);
                if (Ender(p))
                {
                    Cinema.Title(Title(p), Color.Lerp(Primordials.Prism(), Color.white, 0.5f), 1.8f, Body(HAtk) + new Vector2(0f, 3f * _k), 0.9f);
                }
            }
            if (Ender(p))
            {
                Skirmish(dt);
            }
            bool done;
            switch (p)
            {
                case "cleaver": done = Cleaver(dt); break;
                case "starfall": done = Starfall(dt); break;
                case "collapse": done = Collapse(dt); break;
                case "singularity": done = Singularity(dt); break;
                case "sunfall": done = Sunfall(dt); break;
                case "fire": done = WorldFire(dt); break;
                case "tide": done = Tide(dt); break;
                case "frost": done = Frost(dt); break;
                case "shatter": done = Shatter(dt); break;
                case "blades": done = Blades(dt); break;
                case "charge": done = WorldCharge(dt); break;
                case "instant": done = FrozenInstant(dt); break;
                case "gravity": done = Gravity(dt); break;
                case "mountain": done = MountainHurl(dt); break;
                case "serpents": done = Serpents(dt); break;
                case "echoes": done = EchoBarrage(dt); break;
                case "tear": done = SkyTear(dt); break;
                case "clash": done = ClashBout(); break;
                case "rush": done = Rush(dt); break;
                case "collisions": done = Collisions(dt); break;
                case "skyduel": done = SkyDuel(dt); break;
                case "throw": done = Throw(dt); break;
                case "beams": done = Beams(dt); break;
                case "orbs": done = Orbs(dt); break;
                case "comet": done = Comet(dt); break;
                default: done = Chase(dt); break;
            }
            Wake(_ha, dt);
            Wake(_hb, dt);
            if (done || _pt > 14f)
            {
                _power++;
                foreach (Hold h in new[] { _ha, _hb })
                {
                    if (h != null && !h.Finished)
                    {
                        h.Pose.Rot = 0f;
                        h.Pose.SX = h.Pose.SY = 1f;
                        h.Pose.Nudge = Vector2.zero;
                        h.Pose.Hidden = false;
                    }
                }
                if (!Ender(p))
                {
                    _aAttacks = !_aAttacks;
                }
                // the fight goes on wherever it has got to
                if (_ha != null && _hb != null)
                {
                    _center = Inside((_ha.Pose.Ground + _hb.Pose.Ground) * 0.5f);
                    Vector2 d = _hb.Pose.Ground - _ha.Pose.Ground;
                    if (d.sqrMagnitude > 0.5f && Mathf.Abs(d.normalized.x) > 0.3f)
                    {
                        _axis = d.normalized;
                    }
                }
                Next(_power >= _powers.Count ? Phase.Final : Phase.Power);
            }
        }

        // ------------------------------------------------------------------------- movement --

        private int _hits, _bp;
        private bool _met;
        private float _bt;
        private Vector2 _p0, _p1, _p2;
        private readonly Dictionary<Hold, Vector2> _was = new Dictionary<Hold, Vector2>();
        private readonly Dictionary<Hold, float> _boom = new Dictionary<Hold, float>();

        /// <summary>A point well inside the world (a body is never flung off its edge).</summary>
        private static Vector2 Inside(Vector2 p)
        {
            return new Vector2(Mathf.Clamp(p.x, 8f, MapBox.width - 9f), Mathf.Clamp(p.y, 8f, MapBox.height - 9f));
        }

        private static bool Land(Vector2 p)
        {
            WorldTile t = Combat.TileAt(Inside(p));
            return t != null && t.Type != null && !t.Type.liquid;
        }

        /// <summary>
        /// About 'dist' tiles from 'from' toward 'dir', turned as little as it takes to have land on the way
        /// and under the end (they take the fight to what is still standing, not out over a sea they made).
        /// </summary>
        private static Vector2 Landward(Vector2 from, Vector2 dir, float dist)
        {
            Vector2 best = Inside(from + dir * dist);
            int bs = -1;
            for (int i = 0; i < 12; i++)
            {
                float ang = ((i + 1) / 2) * 30f * (i % 2 == 0 ? 1f : -1f);
                Vector2 to = Inside(from + Choreo.Rotate(dir, ang) * dist);
                int sc = 0;
                for (int k = 1; k <= 4; k++)
                {
                    if (Land(Vector2.Lerp(from, to, k / 4f)))
                    {
                        sc += k == 4 ? 3 : 1;
                    }
                }
                if (sc > bs)
                {
                    bs = sc;
                    best = to;
                    if (sc == 6)
                    {
                        break;
                    }
                }
            }
            return best;
        }

        /// <summary>A random point within 'r' of 'c', on land if any of a few tries is.</summary>
        private static Vector2 RandomLand(Vector2 c, float r)
        {
            Vector2 p = Inside(c + Random.insideUnitCircle * r);
            for (int i = 0; i < 6 && !Land(p); i++)
            {
                p = Inside(c + Random.insideUnitCircle * r);
            }
            return p;
        }

        /// <summary>The nearest land to 'c' (rings outward), or 'c' itself if there is none left near.</summary>
        private static Vector2 LandNear(Vector2 c, float r)
        {
            if (Land(c))
            {
                return c;
            }
            for (float rr = 8f; rr <= r; rr += 8f)
            {
                float off = Random.value * 6.283f;
                for (int i = 0; i < 16; i++)
                {
                    float a = off + i * 0.3927f;
                    Vector2 p = Inside(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr);
                    if (Land(p) && Land(p + new Vector2(3f, 0f)) && Land(p - new Vector2(3f, 0f)))
                    {
                        return p;
                    }
                }
            }
            return c;
        }

        /// <summary>The ground under them is gone: they tear through space to land that still stands.</summary>
        private void Relocate()
        {
            Vector2 c2 = LandNear(_center, 140f);
            if ((c2 - _center).sqrMagnitude < 1f)
            {
                return;
            }
            _center = c2;
            foreach (Hold h in new[] { _ha, _hb })
            {
                if (h == null || h.Finished)
                {
                    continue;
                }
                if (Vis(Body(h), 1f))
                {
                    DuelFx.Blink(h.Pose.Ground, _px, _k, Color.white, Primordials.Prism());
                }
                h.Pose.Ground = h == _ha ? PosA : PosB;
                h.Pose.Height = Air;
                _was[h] = h.Pose.Ground;
                if (Vis(Body(h), 1f))
                {
                    DuelFx.Blink(h.Pose.Ground, _px, _k, Color.white, Primordials.Prism(0.5f));
                }
            }
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", Combat.TileAt(_center));
            if (Dev.On) Dev.Trace("world's end: the fight moves to " + _center);
        }

        /// <summary>
        /// Wherever a Primordial moves, the world is torn up: low, its path is a trench and everything in it
        /// dies; high, its wake is a sonic boom that flattens the land under it.
        /// </summary>
        private void Wake(Hold h, float dt)
        {
            if (h == null || h.Finished || h.Pose.Hidden)
            {
                if (h != null)
                {
                    _was[h] = h.Pose.Ground;
                }
                return;
            }
            Vector2 now = h.Pose.Ground;
            if (!_was.TryGetValue(h, out Vector2 was))
            {
                _was[h] = now;
                return;
            }
            _was[h] = now;
            float d = (now - was).magnitude;
            if (d < 0.25f || d > 30f)
            {
                return;
            }
            DoomAround(now, 3f);
            if (Settings.Destruction <= 0)
            {
                return;
            }
            if (h.Pose.Height < 2.5f * _k)
            {
                Kit.Trench(was, now, 2.6f, 3, null, false);
                if (Vis(now, 1f))
                {
                    Spec.Rocks(now, _px, 3, 90f, _k);
                    Kit.DustRing(now, _px, Combat.TileAt(now), 3, 30f, 0.5f, _k);
                }
            }
            else
            {
                _boom.TryGetValue(h, out float b);
                b += d;
                if (b >= 4f)
                {
                    b = 0f;
                    Kit.Ravage(now, 3f, 2, null, false);
                    if (Vis(now, 1f))
                    {
                        Fx.Ring(now, _px, 0.4f, 2f, 40f * _k, 2, 1, FxLayer.Ground, 5, Art.Ground, 2.2f, Fx.WithA(Color.white, 0.8f), Fx.WithA(Primordials.Prism(), 0f));
                    }
                }
                _boom[h] = b;
            }
            if (Vis(Body(h), 1f) && Random.value < 0.7f)
            {
                Trails.Ghost(h.A, 0.18f, Fx.WithA(Primordials.Prism(Random.value), 0.75f), Fx.WithA(Primordials.Void, 0f));
            }
        }

        /// <summary>Moves 'h' toward 'to' (and its height toward 'height') at 'speed' tiles a second; true when there.</summary>
        private bool Go(Hold h, Vector2 to, float height, float speed, float dt, bool lean = true)
        {
            if (h == null || h.Finished)
            {
                return true;
            }
            to = Inside(to);
            Vector2 d = to - h.Pose.Ground;
            float step = speed * dt;
            if (d.magnitude <= step)
            {
                h.Pose.Ground = to;
            }
            else
            {
                h.Pose.Ground += d.normalized * step;
            }
            h.Pose.Height = Mathf.MoveTowards(h.Pose.Height, height, Mathf.Max(step, 6f * dt));
            h.Pose.HideShadow = h.Pose.Height > 0.05f;
            if (Mathf.Abs(d.x) > 0.05f)
            {
                h.Pose.FaceRight = d.x > 0f;
            }
            if (lean)
            {
                h.Pose.Rot = Mathf.Clamp(d.magnitude * 3f, 0f, 25f);
            }
            return (h.Pose.Ground - to).sqrMagnitude < 0.01f && Mathf.Abs(h.Pose.Height - height) < 0.05f;
        }

        private static void FaceTo(Hold h, Vector2 at)
        {
            float dx = at.x - h.Pose.Ground.x;
            if (Mathf.Abs(dx) > 0.05f)
            {
                h.Pose.FaceRight = dx > 0f;
            }
        }

        /// <summary>Two of them meet with a blow at 'at': a burst in the air, the land under it blasted, both schools' light.</summary>
        private void Impact(Vector2 airAt, Vector2 ground, float craterTiles, int force, bool big)
        {
            if (Vis(airAt, 1f))
            {
                DuelFx.Starburst(airAt, _px, (big ? 3.5f : 2f) * _k, Color.white, Primordials.Prism(), Primordials.Prism(0.5f), 0.1f);
                Kit.Flash(airAt, _px, Mathf.RoundToInt((big ? 14f : 8f) * _k), 0.08f, Color.white);
                Fx.Ring(ground, _px, 0.5f, 2f, craterTiles / _px * 1.8f, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.2f, Color.white, Fx.WithA(Primordials.Prism(), 0.6f), Fx.WithA(Primordials.Void, 0f));
                Spec.Rocks(ground, _px, big ? 18 : 8, 110f, _k);
                Kit.Shake(airAt, big ? 0.4f : 0.2f, big ? 0.015f : 0.008f);
            }
            if (Settings.Destruction > 0)
            {
                Kit.Ravage(ground, craterTiles, force, null, false);
            }
            DoomAround(ground, craterTiles + 1f);
            if (big)
            {
                ScarDisk(ground, craterTiles * 1.8f, 0.1f);
            }
            Choreo.Sound(big ? "event:/SFX/EXPLOSIONS/ExplosionBig" : "event:/SFX/HIT/HitGeneric", Combat.TileAt(Inside(ground)));
        }

        /// <summary>The power or the blow lands on the defender: a share of its life (the loser's faster).</summary>
        private void Hurt(float frac)
        {
            Wound(frac);
        }

        // ---------------------------------------------------------------------- anime bouts --

        /// <summary>Hyper Rush: a blur across the land, a storm of blows, and the last one sends the other flying.</summary>
        private bool Rush(float dt)
        {
            Hold a = HAtk, d = HDef;
            switch (_bp)
            {
                case 0:
                    FaceTo(d, a.Pose.Ground);
                    if (Go(a, d.Pose.Ground - (d.Pose.Ground - a.Pose.Ground).normalized * 0.9f * _k, d.Pose.Height, 70f, dt) || _pt > 0.5f)
                    {
                        _bp = 1;
                        _bt = 0f;
                    }
                    break;
                case 1:
                    {
                        _bt += dt;
                        // blows from every side, faster than sight
                        if (_bt >= 0.07f)
                        {
                            _bt = 0f;
                            _hits++;
                            Vector2 o = Random.insideUnitCircle.normalized * 0.9f * _k;
                            a.Pose.Ground = Inside(d.Pose.Ground + o);
                            FaceTo(a, d.Pose.Ground);
                            a.Pose.Rot = Random.Range(-25f, 25f);
                            d.Pose.Nudge = Random.insideUnitCircle * 0.8f;
                            Impact(Body(d), d.Pose.Ground + Random.insideUnitCircle * 3f * _k, 2f, 2, false);
                            Strike(Attacker, Defender, 0.003f);
                        }
                        if (_hits >= 18)
                        {
                            // the last blow
                            d.Pose.Nudge = Vector2.zero;
                            Vector2 away = (d.Pose.Ground - a.Pose.Ground).normalized;
                            if (away.sqrMagnitude < 0.1f)
                            {
                                away = _axis;
                            }
                            _p0 = Landward(d.Pose.Ground, away, 30f);
                            Impact(Body(d), d.Pose.Ground, 5f, 3, true);
                            Kit.Stop(Body(d), 0.15f);
                            Hurt(0.04f);
                            _bp = 2;
                        }
                        break;
                    }
                case 2:
                    {
                        // flung across the land, ploughing it; the other right behind
                        bool there = Go(d, _p0, 0.6f * _k, 55f, dt, false);
                        d.Pose.Rot += 900f * dt;
                        Go(a, _p0 - (_p0 - a.Pose.Ground).normalized * 6f * _k, Air, 35f, dt);
                        if (there)
                        {
                            Impact(Body(d), _p0, 7f, 3, true);
                            d.Pose.Rot = 0f;
                            _bp = 3;
                            _bt = 0f;
                        }
                        break;
                    }
                default:
                    _bt += dt;
                    Go(d, d.Pose.Ground, Air, 10f, dt);
                    FaceTo(d, a.Pose.Ground);
                    FaceTo(a, d.Pose.Ground);
                    return _bt > 0.3f;
            }
            return false;
        }

        /// <summary>Thousand Collisions: they vanish, and meet ten times all over the land, each a crater.</summary>
        private bool Collisions(float dt)
        {
            Hold a = _ha, b = _hb;
            _bt += dt;
            if (_bp == 0)
            {
                foreach (Hold h in new[] { a, b })
                {
                    if (Vis(Body(h), 1f))
                    {
                        DuelFx.Blink(h.Pose.Ground, _px, _k, Color.white, Primordials.Prism());
                    }
                    h.Pose.Hidden = true;
                }
                _bp = 1;
                _bt = 0f;
                _p2 = _center;
            }
            float beat = Mathf.Lerp(0.3f, 0.12f, _hits / 10f);
            if (_bt >= beat && _hits < 10)
            {
                _bt = 0f;
                _hits++;
                Vector2 at = RandomLand(_p2, 22f);
                _p2 = Vector2.Lerp(_p2, at, 0.5f);
                Vector2 dir = Random.insideUnitCircle.normalized;
                a.Pose.Hidden = b.Pose.Hidden = false;
                a.Pose.Ground = Inside(at - dir * 0.6f * _k);
                b.Pose.Ground = Inside(at + dir * 0.6f * _k);
                a.Pose.Height = b.Pose.Height = Random.Range(0.5f, 4f) * _k;
                FaceTo(a, b.Pose.Ground);
                FaceTo(b, a.Pose.Ground);
                a.Pose.Rot = b.Pose.Rot = 22f;
                _was[a] = a.Pose.Ground;
                _was[b] = b.Pose.Ground;
                Impact((Body(a) + Body(b)) * 0.5f, at, 6f, 3, true);
                Strike(_a, _b, 0.006f);
                Strike(_b, _a, 0.006f);
            }
            else if (_bt > 0.06f && _hits < 10)
            {
                a.Pose.Hidden = b.Pose.Hidden = true;
            }
            if (_hits >= 10 && _bt > 0.3f)
            {
                a.Pose.Hidden = b.Pose.Hidden = false;
                _center = Inside(_p2);
                return true;
            }
            return false;
        }

        /// <summary>Sky Duel: they take it high into the sky; every blow up there flattens the land under it; then one is spiked down.</summary>
        private bool SkyDuel(float dt)
        {
            Hold a = HAtk, d = HDef;
            float hi = 9f * _k;
            switch (_bp)
            {
                case 0:
                    {
                        bool up1 = Go(a, a.Pose.Ground, hi, 20f, dt), up2 = Go(d, d.Pose.Ground, hi, 20f, dt);
                        if (up1 && up2 || _pt > 1f)
                        {
                            _bp = 1;
                            _bt = 1f;   // (the first meeting at once)
                        }
                        break;
                    }
                case 1:
                    {
                        _bt += dt;
                        if (_bt >= 0.45f)
                        {
                            if (_hits >= 6)
                            {
                                _bp = 2;
                                break;
                            }
                            // a new place in the sky, and they meet there
                            _bt = 0f;
                            _hits++;
                            _met = false;
                            Vector2 at = RandomLand(_center, 14f);
                            Vector2 dir = Random.insideUnitCircle.normalized;
                            _p0 = at - dir * 0.6f * _k;
                            _p1 = at + dir * 0.6f * _k;
                            _p2 = at;
                        }
                        if (_hits == 0)
                        {
                            break;
                        }
                        bool m1 = Go(a, _p0, hi, 70f, dt), m2 = Go(d, _p1, hi, 70f, dt);
                        FaceTo(a, _p1);
                        FaceTo(d, _p0);
                        if (!_met && (m1 && m2 || _bt > 0.36f))
                        {
                            _met = true;
                            a.Pose.Rot = d.Pose.Rot = 25f;
                            Impact((Body(a) + Body(d)) * 0.5f, _p2, 6f, 2, true);
                            Strike(Attacker, Defender, 0.006f);
                            Strike(Defender, Attacker, 0.004f);
                        }
                        break;
                    }
                case 2:
                    {
                        // above it, and down: spiked into the ground
                        Go(a, d.Pose.Ground, hi + 1.5f * _k, 40f, dt);
                        if (Go(d, d.Pose.Ground, 0f, 60f, dt, false))
                        {
                            Impact(Body(d), d.Pose.Ground, 10f, 4, true);
                            Blast(d.Pose.Ground, 18, false, Attacker);
                            Hurt(0.06f);
                            Kit.Stop(Body(d), 0.2f);
                            _bp = 3;
                            _bt = 0f;
                        }
                        d.Pose.Rot += 1200f * dt;
                        break;
                    }
                default:
                    _bt += dt;
                    d.Pose.Rot = 0f;
                    Go(a, a.Pose.Ground, Air, 12f, dt);
                    Go(d, d.Pose.Ground, Air, 8f, dt);
                    return _bt > 0.35f;
            }
            return false;
        }

        /// <summary>Continental Throw: seized, swung, and hurled across the world, ploughing it; the thrower is waiting where it lands.</summary>
        private bool Throw(float dt)
        {
            Hold a = HAtk, d = HDef;
            switch (_bp)
            {
                case 0:
                    if (Go(a, d.Pose.Ground - (d.Pose.Ground - a.Pose.Ground).normalized * 0.7f * _k, d.Pose.Height, 60f, dt) || _pt > 0.5f)
                    {
                        _bp = 1;
                        _bt = 0f;
                        _p2 = a.Pose.Ground;
                        Choreo.Sound("event:/SFX/OTHER/Punch", Combat.TileAt(Inside(_p2)));
                    }
                    break;
                case 1:
                    {
                        // round and round over its head
                        _bt += dt;
                        float ang = _bt * 18f;
                        d.Pose.Ground = Inside(_p2 + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.5f) * 1.6f * _k);
                        d.Pose.Height = a.Pose.Height + 1f * _k;
                        d.Pose.Rot = ang * Mathf.Rad2Deg;
                        a.Pose.Rot = Mathf.Sin(ang) * 20f;
                        _was[d] = d.Pose.Ground;
                        if (Vis(Body(a), 1f) && Random.value < 0.5f)
                        {
                            Fx.Ring(_p2, _px, 0.3f, 2f, 30f * _k, 2, 1, FxLayer.Ground, 5, Art.Ground, 2f, Fx.WithA(Color.white, 0.6f), Fx.WithA(Primordials.Prism(), 0f));
                        }
                        if (_bt >= 0.7f)
                        {
                            Vector2 dir = Choreo.Rotate(_axis, Random.Range(-60f, 60f));
                            _p0 = Landward(_p2, dir, 45f);
                            Impact(Body(d), _p2, 4f, 3, true);
                            _bp = 2;
                        }
                        break;
                    }
                case 2:
                    {
                        // through the world, low: a trench with a river of lava in it the whole way
                        Vector2 was = d.Pose.Ground;
                        bool there = Go(d, _p0, 0.5f * _k, 70f, dt, false);
                        d.Pose.Rot += 1000f * dt;
                        if (Settings.Destruction > 0)
                        {
                            Kit.Trench(was, d.Pose.Ground, 3.2f, 4, null, false);
                            Lava(was, d.Pose.Ground);
                        }
                        if (there)
                        {
                            // and the thrower is already there
                            if (Vis(Body(a), 1f))
                            {
                                DuelFx.Blink(a.Pose.Ground, _px, _k, Color.white, Primordials.Prism());
                            }
                            a.Pose.Ground = Inside(_p0 + new Vector2(0.4f, 0f) * _k);
                            a.Pose.Height = 3f * _k;
                            _was[a] = a.Pose.Ground;
                            _bp = 3;
                            _bt = 0f;
                        }
                        break;
                    }
                case 3:
                    _bt += dt;
                    d.Pose.Rot = 0f;
                    if (Go(a, _p0, 0.2f * _k, 40f, dt) || _bt > 0.3f)
                    {
                        Impact(Body(d), _p0, 9f, 4, true);
                        Blast(_p0, 15, false, Attacker);
                        Hurt(0.07f);
                        _bp = 4;
                        _bt = 0f;
                    }
                    break;
                default:
                    _bt += dt;
                    Go(a, a.Pose.Ground, Air, 10f, dt);
                    Go(d, d.Pose.Ground + (d.Pose.Ground - a.Pose.Ground).normalized * 4f * _k, Air, 12f, dt);
                    return _bt > 0.35f;
            }
            return false;
        }

        /// <summary>Beam Clash: two beams meet between them and push; the meeting point goes off.</summary>
        private bool Beams(float dt)
        {
            Hold a = _ha, b = _hb;
            Vector2 c = _center;
            if (_bp == 0)
            {
                bool m1 = Go(a, c - _axis * 9f * _k, Air, 40f, dt), m2 = Go(b, c + _axis * 9f * _k, Air, 40f, dt);
                FaceTo(a, b.Pose.Ground);
                FaceTo(b, a.Pose.Ground);
                if (m1 && m2 || _pt > 0.8f)
                {
                    _bp = 1;
                    _bt = 0f;
                }
                return false;
            }
            _bt += dt;
            Vector2 ba = Body(a), bb = Body(b);
            if (_bp == 1)
            {
                float push = 0.5f + Mathf.Sin(_bt * 6f) * 0.15f + (_bt / 2.4f) * (Attacker == _a ? 0.2f : -0.2f);
                Vector2 meet = Vector2.Lerp(ba, bb, push);
                Beam(ba, meet, 4, 0.05f);
                Beam(bb, meet, 4, 0.05f);
                if (Vis(meet, 1f))
                {
                    Spec.Blob(meet, (1.5f + _bt) * _k, _px, 0.06f, FxLayer.Top, 26, 1f, 0.4f, Color.white, Fx.WithA(Primordials.Prism(), 0.7f));
                }
                // the land under the meeting point boils
                if (Settings.Destruction > 0 && Random.value < 0.4f)
                {
                    Kit.Ravage(Inside(new Vector2(meet.x, meet.y - Air)) + Random.insideUnitCircle * 3f, 3f, 3, null, false);
                }
                if (_bt >= 2.4f)
                {
                    Vector2 g = Inside(new Vector2(meet.x, meet.y - Air));
                    Burst(meet, 14f * _k, true);
                    Blast(g, 22, false, null);
                    Strike(_a, _b, 0.02f);
                    Strike(_b, _a, 0.02f);
                    Hurt(0.03f);
                    _bp = 2;
                    _bt = 0f;
                }
                return false;
            }
            // both thrown back from it
            Go(a, a.Pose.Ground - _axis * 5f * _k * dt * 3f, Air, 15f, dt);
            Go(b, b.Pose.Ground + _axis * 5f * _k * dt * 3f, Air, 15f, dt);
            return _bt > 0.3f;
        }

        /// <summary>Orb Barrage: they circle each other high and fast, firing volleys; every orb blasts a crater.</summary>
        private bool Orbs(float dt)
        {
            _bt += dt;
            float ang = _pt * 1.6f;
            float r = 11f * _k;
            Go(_ha, _center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.6f) * r, Air + 1.5f * _k, 50f, dt);
            Go(_hb, _center - new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.6f) * r, Air + 1.5f * _k, 50f, dt);
            FaceTo(_ha, _hb.Pose.Ground);
            FaceTo(_hb, _ha.Pose.Ground);
            if (_bt >= 0.1f && _pt < 3.2f)
            {
                _bt = 0f;
                _hits++;
                bool fromA = (_hits & 1) == 0;
                Hold s = fromA ? _ha : _hb, t = fromA ? _hb : _ha;
                Vector2 to = t.Pose.Ground + Random.insideUnitCircle * 4f * _k;
                Vector2 from = Body(s);
                if (Vis(from, 1f) || Vis(to, 1f))
                {
                    PrimFx.Orb(from, to, 0.45f * _k, 0.18f, Primordials.Prism(Random.value, 0.7f), FxLayer.Top, 24, false);
                }
                _falls.Add(new Vector3(to.x, to.y, _pt + 0.18f));
                Strike(s.A, t.A, 0.002f);
            }
            for (int i = _falls.Count - 1; i >= 0; i--)
            {
                if (_pt >= _falls[i].z)
                {
                    Vector2 at = new Vector2(_falls[i].x, _falls[i].y);
                    _falls.RemoveAt(i);
                    Impact(at + new Vector2(0f, 0.5f), at, 4f, 3, false);
                }
            }
            return _pt >= 3.6f && _falls.Count == 0;
        }

        /// <summary>Comet Dive: it climbs out of sight, and comes back down onto the other like a comet.</summary>
        private bool Comet(float dt)
        {
            Hold a = HAtk, d = HDef;
            switch (_bp)
            {
                case 0:
                    {
                        // up and out of sight
                        if (Go(a, a.Pose.Ground, 30f * _k, 45f, dt) || _pt > 1f)
                        {
                            _bp = 1;
                            _bt = 0f;
                            if (Vis(Body(a), 2f))
                            {
                                Fx.Ring(a.Pose.Ground, _px, 0.5f, 2f, 50f * _k, 3, 1, FxLayer.Ground, 6, Art.Ground, 2f, Color.white, Fx.WithA(Primordials.Prism(), 0f));
                            }
                        }
                        break;
                    }
                case 1:
                    _bt += dt;
                    a.Pose.Ground = Vector2.Lerp(a.Pose.Ground, d.Pose.Ground, 0.2f);
                    if (_bt > 0.5f)
                    {
                        _bp = 2;
                    }
                    break;
                case 2:
                    {
                        // the dive
                        Vector2 top = Body(a);
                        bool down = Go(a, d.Pose.Ground, d.Pose.Height, 120f, dt);
                        if (Vis(Body(d), 2f))
                        {
                            DuelFx.Comet(top + new Vector2(0f, 6f * _k), Body(a), _px * 1.6f, Color.white, Primordials.Prism());
                            PrimFx.Streak(top + new Vector2(0f, 6f * _k), Body(a), 0.7f * _k, 0.14f, FxLayer.Top, 21, false, Primordials.Prism());
                        }
                        if (down)
                        {
                            Impact(Body(d), d.Pose.Ground, 12f, 4, true);
                            Blast(d.Pose.Ground, 30, true, Attacker);
                            Primordials.Flash(Fx.WithA(Color.white, 0.7f), 0.3f);
                            Hurt(0.08f);
                            Kit.Stop(Body(d), 0.2f);
                            _bp = 3;
                            _bt = 0f;
                        }
                        break;
                    }
                default:
                    _bt += dt;
                    Go(a, a.Pose.Ground, Air, 10f, dt);
                    Go(d, d.Pose.Ground + (d.Pose.Ground - a.Pose.Ground).normalized * 5f * _k, Air, 14f, dt);
                    return _bt > 0.4f;
            }
            return false;
        }

        /// <summary>Chase Across the World: one breaks away low over the land, the other runs it down; they trade blows on the run.</summary>
        private bool Chase(float dt)
        {
            Hold a = HAtk, d = HDef;
            if (_bp == 0)
            {
                Vector2 away = d.Pose.Ground - a.Pose.Ground;
                away = away.sqrMagnitude > 0.01f ? away.normalized : _axis;
                Vector2 dir = Choreo.Rotate(away, Random.Range(-70f, 70f));
                _p0 = Landward(d.Pose.Ground, dir, 55f);
                _bp = 1;
                _bt = 0f;
            }
            _bt += dt;
            bool there = Go(d, _p0, 1.2f * _k, 26f, dt);
            Go(a, d.Pose.Ground - (d.Pose.Ground - a.Pose.Ground).normalized * 1.2f * _k, 1.2f * _k, 30f, dt);
            if (_bt >= 0.4f)
            {
                _bt = 0f;
                // a blow on the run, the land blasted under them
                Impact((Body(a) + Body(d)) * 0.5f, d.Pose.Ground, 5f, 3, true);
                Strike(Attacker, Defender, 0.006f);
            }
            if (there)
            {
                Hurt(0.02f);
                return true;
            }
            return _pt > 4f;
        }

        private static string Title(string p)
        {
            string more = MoreTitle(p);
            if (more != null)
            {
                return more;
            }
            switch (p)
            {
                case "cleaver": return "PLANET CLEAVER";
                case "starfall": return "STARFALL APOCALYPSE";
                case "collapse": return "CONTINENTAL COLLAPSE";
                case "singularity": return "SINGULARITY";
                case "sunfall": return "SUNFALL";
                default: return "WORLD FIRE";
            }
        }

        /// <summary>A blow between the two: a share of 'v's life, but never the last of it before the end.</summary>
        private void Strike(Actor by, Actor v, float frac)
        {
            if (v == null || !v.isAlive() || _ph >= Phase.Final)
            {
                return;
            }
            float max = Mathf.Max(1f, v.getMaxHealth());
            float room = (v.getHealth() - 0.12f * max) / max;
            if (room > 0f)
            {
                // (both are held in intangible moves the whole battle: the blow must pierce them to land)
                Primordials.Smite(by, v, Mathf.Min(frac * 0.55f, room), true);
            }
        }

        /// <summary>
        /// A fighter let go of mid-battle (picked up by the god hand, taken over by the player) is held
        /// again once it is free, where it is now: without its hold every later step found it "arrived"
        /// at once and the rounds rushed through.
        /// </summary>
        private static Hold Rehold(Hold h, Actor x)
        {
            if (h != null && !h.Ended)
            {
                return h;
            }
            if (x == null || !x.isAlive() || x.isInMagnet() || x.isInsideSomething() || ControllableUnit.isControllingUnit(x))
            {
                return h;
            }
            if (Moves.TryGet(x, out Move m))
            {
                Moves.Cancel(m);
            }
            Hold n = new Hold(x);
            n.Pose.Ground = x.current_position;
            return Moves.Start(n) ? n : h;
        }

        private static void Floor(Actor x)
        {
            if (x != null && x.isAlive() && x.getHealth() < 0.1f * x.getMaxHealth())
            {
                x.setHealth(Mathf.CeilToInt(0.1f * x.getMaxHealth()));
            }
        }

        /// <summary>The power lands on the other Primordial: its share of its life (the loser's goes faster).</summary>
        private void Wound(float frac)
        {
            Actor d = Defender;
            bool loser = d == (_aWins ? _b : _a);
            Strike(Attacker, d, loser ? frac * 1.15f : frac * 0.75f);
            Hold h = HDef;
            if (h != null && Vis(Body(h), 1f))
            {
                Trails.Ghost(d, 0.25f, Fx.WithA(Color.white, 0.95f), Fx.WithA(Primordials.Prism(), 0f));
            }
        }

        /// <summary>Planet Cleaver: one cut through the other and on across the world, the land opening along it.</summary>
        private bool Cleaver(float dt)
        {
            Hold atk = HAtk;
            Vector2 from = Body(atk);
            if (_pt < 1f)
            {
                // the blade of first light grows over its head
                float k = _pt;
                atk.Pose.Rot = -12f * k;
                if (Vis(from, 1f))
                {
                    Beam(from, from + new Vector2(0f, (2f + 10f * k) * _k), 1 + (int)(2f * k), 0.05f);
                }
                return false;
            }
            if (_cutDone <= 0f)
            {
                // the line: through the other, out to the edges of the world
                Vector2 dir = (Body(HDef) - from).normalized;
                if (dir.sqrMagnitude < 0.1f)
                {
                    dir = _axis;
                }
                dir = Choreo.Rotate(dir, Random.Range(-25f, 25f));
                Vector2 p = Defender.current_position;
                float big = MapBox.width + MapBox.height;
                _cutA = Combat.Clamp(p - dir * big);
                _cutB = Combat.Clamp(p + dir * big);
                atk.Pose.Rot = 30f;
                Beam(from, Body(HDef), 4, 0.4f);

                Primordials.Flash(Fx.WithA(Color.white, 0.6f), 0.25f);
                Burst(Body(HDef), 5f * _k, false);
                Wound(0.14f);
                Choreo.Sound("event:/SFX/HIT/HitSwordSword", Combat.TileAt(p));
                _cutDone = 0.001f;
            }
            // the land opens along it, from the other out to both edges
            Vector2 mid = Defender.current_position;
            float len = Mathf.Max((_cutA - mid).magnitude, (_cutB - mid).magnitude);
            float reach = Mathf.Min(len, (_pt - 1f) * 160f);
            while (_cutDone < reach)
            {
                float s = _cutDone;
                _cutDone += 6f;
                foreach (Vector2 end in new[] { _cutA, _cutB })
                {
                    Vector2 dir = (end - mid);
                    float max = dir.magnitude;
                    if (s > max)
                    {
                        continue;
                    }
                    dir /= Mathf.Max(0.01f, max);
                    Vector2 a = mid + dir * s, b = mid + dir * Mathf.Min(max, s + 6f);
                    if (Settings.Destruction > 0)
                    {
                        Kit.Trench(a, b, 4f * WorldScale, 4, Attacker, true);
                        Lava(a, b);
                        ScarDisk((a + b) * 0.5f, 9f * WorldScale, 0.12f);
                    }
                    DoomAround((a + b) * 0.5f, 4f * WorldScale);
                    if (Vis(b, 1f))
                    {
                        // the cut racing on: a blade of light at its front, the land glowing open behind it
                        PrimFx.Slash(b, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, 9f * _k, 0.7f * _k, 0.3f, FxLayer.Top, 22, false, Primordials.Prism(s * 0.01f));
                        PrimFx.Crack(a, dir, (b - a).magnitude, 4f * _px, 2f, new Color(1f, 0.55f, 0.25f, 1f), FxLayer.Ground, 3, false, false, 0);
                        Kit.Flash(b, _px, 8, 0.1f, new Color(1f, 0.6f, 0.3f, 1f));
                        Spec.Rocks(b, _px, 8, 110f, _k);
                    }
                }
            }
            return _cutDone >= len && _pt >= 3f;
        }

        /// <summary>The middle of a cut turns to a river of lava.</summary>
        private static void Lava(Vector2 a, Vector2 b)
        {
            float l = (b - a).magnitude;
            int n = Mathf.CeilToInt(l);
            for (int i = 0; i <= n; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, n == 0 ? 0f : i / (float)n);
                WorldTile t = Combat.TileAt(p);
                if (t == null || t.Type == null || t.Type.liquid && !t.Type.lava)
                {
                    continue;
                }
                MapAction.terraformMain(t, TileLibrary.lava3, TerraformLibrary.nothing);
            }
        }

        /// <summary>Starfall Apocalypse: meteors on the whole world, the other buried under a share of them.</summary>
        private bool Starfall(float dt)
        {
            Hold atk = HAtk;
            atk.Pose.Rot = -8f;
            atk.Pose.Height += 2f * _k * dt;
            int total = Mathf.RoundToInt(200f * WorldScale * WorldScale);
            const float span = 6.5f, fall = 1.35f;
            int want = Mathf.Min(total, (int)(_pt / span * total));
            while (_spawned < want)
            {
                _spawned++;
                // most anywhere on the land of the world, every tenth on the other
                Vector2 at = _spawned % 10 == 0 ? Defender.current_position + Random.insideUnitCircle * 3f
                    : new Vector2(Random.Range(4f, MapBox.width - 4f), Random.Range(4f, MapBox.height - 4f));
                _falls.Add(new Vector3(at.x, at.y, _pt + fall));
                if (Vis(at, 1f))
                {
                    Areas.Add(new MeteorHazard(Attacker, atk, Hue.For(Attacker, School.EarthBreaker, 3), Scale.Ultimate, at, 3f, 0f, 2f, SkyFall.Rock));
                }
            }
            for (int i = _falls.Count - 1; i >= 0; i--)
            {
                if (_pt >= _falls[i].z)
                {
                    Vector2 at = new Vector2(_falls[i].x, _falls[i].y);
                    _falls.RemoveAt(i);
                    WorldTile t = Combat.TileAt(at);
                    if (t != null && Settings.Destruction > 0)
                    {
                        MapAction.damageWorld(t, 7, TerraformLibrary.bomb, Attacker);
                    }
                    DoomAround(at, 6f);
                    ScarDisk(at, Random.Range(7f, 12f), 0.15f);
                    if ((Defender.current_position - at).sqrMagnitude < 16f)
                    {
                        Wound(0.02f);
                    }
                }
            }
            return _spawned >= total && _falls.Count == 0;
        }

        /// <summary>Continental Collapse: the land round the other breaks apart and sinks into the sea.</summary>
        private bool Collapse(float dt)
        {
            Vector2 c = Defender.current_position;
            float R = 42f * WorldScale;
            if (_pt < 1.2f)
            {
                // the cracks race out from under it
                if (Random.value < 0.8f)
                {
                    Vector2 dir = Random.insideUnitCircle.normalized;
                    Vector2 to = Combat.Clamp(c + dir * R * Random.Range(0.5f, 1f));
                    if (Vis(c, 1f) || Vis(to, 1f))
                    {
                        PrimFx.Crack(c, (to - c).normalized, (to - c).magnitude, 3f * _px, 1.4f, new Color(1f, 0.4f, 0.1f, 1f), FxLayer.Ground, 3, false, true, 1, 0f, 8f);
                    }
                    if (Settings.Destruction > 0)
                    {
                        Kit.Trench(c, to, 1.5f, 3, Attacker, false);
                    }
                }
                HAtk.Pose.Rot = 20f * Mathf.Sin(_pt * 30f);
                return false;
            }
            if (_sinkDone <= 0f)
            {
                Burst(c, 8f * _k, true);
                Wound(0.15f);
                ScarDisk(c, R * 1.7f, 0.1f);
                if (Settings.Destruction > 0)
                {
                    WorldTile qt = Combat.TileAt(Inside(c + Random.insideUnitCircle.normalized * R * 1.2f));
                    if (qt != null)
                    {
                        Earthquake.startQuake(qt, EarthquakeType.BigDecrease);
                    }
                }
            }
            // ring after ring of the land drops into the sea
            float r = Mathf.Min(R, (_pt - 1.2f) * 18f * WorldScale);
            float from = _sinkDone;
            _sinkDone = Mathf.Max(0.001f, r);
            if (Settings.Destruction > 0)
            {
                Annulus(c, from, r, t =>
                {
                    if (t.hasBuilding() && t.building != null && t.building.isAlive())
                    {
                        t.building.startDestroyBuilding();
                    }
                    if (t.Type != null && !t.Type.liquid)
                    {
                        MapAction.terraformMain(t, TileLibrary.deep_ocean, TerraformLibrary.nothing);
                    }
                });
            }
            DoomAround(c, r);
            if (Vis(c, 1f) && Random.value < 0.5f)
            {
                Vector2 at = c + Random.insideUnitCircle.normalized * r;
                Spec.Rocks(at, _px, 10, 90f, _k);
                Kit.DustRing(at, _px, Combat.TileAt(at), 6, 30f, 0.6f, _k);
            }
            return r >= R;
        }

        /// <summary>Singularity: a black star swallows everything round the other, collapses and goes off.</summary>
        private bool Singularity(float dt)
        {
            Vector2 c = Body(HDef) + new Vector2(0f, 1f * _k);
            const float grow = 3f;
            if (_pt < grow)
            {
                float k = _pt / grow;
                float r = (0.5f + 5.5f * k) * _k;
                if (Vis(c, 2f))
                {
                    Spec.Blob(c, r, _px, 0.06f, FxLayer.Top, 26, 1f, 0.05f, Fx.WithA(Color.black, 1f));
                    Spec.Blob(c, r * 1.35f, _px, 0.06f, FxLayer.Top, 25, 1f, 0.6f, Fx.WithA(Primordials.Prism(), 0.45f));
                    if (Random.value < 0.6f)
                    {
                        Fx.Ring(c, _px, 0.25f, r / _px * 2.6f, r / _px * 1.4f, 2, 1, FxLayer.Top, 24, 0.35f, 2f, Fx.WithA(Primordials.Prism(Random.value), 0f), Fx.WithA(Color.white, 0.8f));
                    }
                    // everything round it drawn in: dust, rocks, light
                    for (int i = 0; i < 4; i++)
                    {
                        Vector2 o = Random.insideUnitCircle.normalized * Random.Range(10f, 30f) * _k;
                        Particle p = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], c + o, _px * 1.5f, 0.5f, FxLayer.Top, 24);
                        p?.Moving(-o * 2f, 0f, 0f).Color(Fx.WithA(Color.white, 0.9f), Fx.WithA(Primordials.Prism(), 0.8f), Fx.WithA(Color.black, 0f));
                    }
                }
                // what is near is already gone into it
                DoomAround(c, 30f * WorldScale * k);
                if (Settings.Destruction > 0 && Random.value < 0.3f)
                {
                    Kit.Ravage(c + Random.insideUnitCircle * 20f * k, 3f, 3, Attacker, false);
                }
                World.world.startShake(0.2f, 0.01f, 1.5f + 2f * k);
                return false;
            }
            if (_pt < grow + 0.35f)
            {
                // it collapses to a point...
                float k = (_pt - grow) / 0.35f;
                if (Vis(c, 2f))
                {
                    Spec.Blob(c, Mathf.Lerp(6f, 0.2f, k) * _k, _px, 0.05f, FxLayer.Top, 26, 1f, 0.05f, Color.black);
                }
                return false;
            }
            if (Enter2())
            {
                // ...and goes off like a dying sun
                Burst(c, 22f * _k, true);
                Blast(Defender.current_position, Big(60), true, Attacker);
                Wound(0.16f);
                Kit.Stop(c, 0.25f);
                Vector2 gc = Defender.current_position;
                ScarDisk(gc, Big(60) * 1.8f, 0.15f);
                if (Settings.Destruction > 0)
                {
                    int n = Random.Range(5, 8);
                    for (int i = 0; i < n; i++)
                    {
                        Vector2 d = Choreo.Rotate(Vector2.right, i * 360f / n + Random.Range(-15f, 15f));
                        Vector2 p0 = gc + d * Big(60) * 0.8f, p1 = gc + Choreo.Rotate(d, Random.Range(-25f, 25f)) * Big(60) * 2.1f;
                        if (OnMap(p0))
                        {
                            Kit.Trench(p0, Inside(p1), 2.5f * WorldScale, 4, Attacker, false);
                            Lava(p0, Inside(p1));
                        }
                    }
                }
            }
            return _pt >= grow + 1.6f;
        }

        private bool _enter2;

        private bool Enter2()
        {
            if (_enter2)
            {
                return false;
            }
            _enter2 = true;
            return true;
        }

        /// <summary>Sunfall: a second sun dragged down out of the sky onto the other.</summary>
        private bool Sunfall(float dt)
        {
            Vector2 ground = Defender.current_position;
            const float down = 3.2f;
            HAtk.Pose.Rot = -14f;
            if (_pt < down)
            {
                float k = Anim.InQuad(_pt / down);
                _sunAt = ground + new Vector2(-14f * (1f - k), 40f * (1f - k) + 1f) * _k;
                float r = 9f * _k;
                if (Vis(_sunAt, 4f) || Vis(ground, 1f))
                {
                    // a sun (pixel art: a turning corona, a face with a darker limb and boiling cells, flares)
                    PrimFx.Sun(_sunAt, r, FxLayer.Top, 25, false, new Color(1f, 0.85f, 0.4f, 1f), new Color(1f, 0.5f, 0.12f, 1f), new Color(1f, 0.65f, 0.2f, 1f));
                    for (int i = 0; i < 3; i++)
                    {
                        Kit.Ember(_sunAt + Random.insideUnitCircle * r, _px, 0.6f, new Vector2(Random.Range(-10f, 10f), Random.Range(10f, 40f)) * _px, new Color(1f, 0.6f, 0.2f, 1f));
                    }
                }
                // the land under it burns long before it lands
                if (Settings.Destruction > 0 && Random.value < 0.6f)
                {
                    WorldTile t = Combat.TileAt(Combat.Clamp(ground + Random.insideUnitCircle * 30f * k));
                    if (t != null && !t.Type.liquid)
                    {
                        t.setBurned();
                        t.startFire(true);
                    }
                }
                if (Random.value < 0.3f)
                {
                    Primordials.Flash(Fx.WithA(new Color(1f, 0.6f, 0.2f, 1f), 0.15f * k), 0.1f);
                }
                return false;
            }
            if (Enter2())
            {
                Primordials.Flash(Fx.WithA(Color.white, 0.95f), 0.6f);
                Burst(ground, 26f * _k, false);
                Blast(ground, Big(70), true, Attacker);
                DoomAround(ground, 55f * WorldScale);
                Wound(0.16f);
                Kit.Stop(ground, 0.3f);
                ScarDisk(ground, Big(70) * 2.4f, 0.35f);
            }
            return _pt >= down + 1.8f;
        }

        /// <summary>World Fire: the first fire, everywhere at once.</summary>
        private bool WorldFire(float dt)
        {
            Hold atk = HAtk;
            atk.Pose.SY = 1.15f;
            atk.Pose.SX = 0.9f;
            if (_pt < 0.6f)
            {
                if (Vis(Body(atk), 1f))
                {
                    Fx.Ring(Body(atk), _px, 0.3f, 2f, 40f * _k, 2, 1, FxLayer.Top, 22, 1f, 2f, Color.white, Fx.WithA(new Color(1f, 0.4f, 0.1f, 1f), 0f));
                }
                return false;
            }
            if (Enter2())
            {
                Primordials.Flash(Fx.WithA(new Color(1f, 0.45f, 0.1f, 1f), 0.5f), 0.4f);
                Burst(Body(atk), 10f * _k, false);
                Wound(0.12f);
            }
            // the fire runs over the world
            int per = Settings.Destruction > 0 ? Mathf.RoundToInt(70f * WorldScale * WorldScale) : 0;
            for (int i = 0; i < per && _fires < 5000f * WorldScale * WorldScale; i++)
            {
                WorldTile t = World.world.GetTileSimple(Random.Range(0, MapBox.width), Random.Range(0, MapBox.height));
                if (t == null || t.Type == null || t.Type.liquid)
                {
                    continue;
                }
                t.setBurned();
                Scorch(t);
                if (t.startFire(true))
                {
                    _fires++;
                }
                if (Random.value < 0.02f)
                {
                    ScarDisk(new Vector2(t.x + 0.5f, t.y + 0.5f), Random.Range(4f, 8f), 0.4f);
                }
                if (t.hasUnits())
                {
                    t.doUnits(new System.Action<Actor>(u => DoomOne(u)));
                }
            }
            if (Random.value < 0.4f)
            {
                Vector2 at = Combat.Clamp(_center + Random.insideUnitCircle * 25f * _k);
                if (Vis(at, 1f))
                {
                    Spec.Column(at, _px, 6, _k * 1.5f, new Color(1f, 0.45f, 0.1f, 1f));
                }
            }
            return _pt >= 4.6f;
        }

        // ------------------------------------------------------------------------------- final --

        private void Final()
        {
            Hold w = _aWins ? _ha : _hb, l = _aWins ? _hb : _ha;
            Actor wa = _aWins ? _a : _b, lo = _aWins ? _b : _a;
            _sep = Mathf.Lerp(_sep, 9f, 0.05f);
            HoverBoth(Air + 1f * _k);
            Vector2 bw = Body(w), bl = Body(l);
            if (_pt < 2.6f)
            {
                // both gather everything they are
                float k = _pt / 2.6f;
                foreach (Hold h in new[] { _ha, _hb })
                {
                    Vector2 hand = Body(h) + new Vector2((h.Pose.FaceRight ? 1f : -1f) * 1f * _k, 0f);
                    if (Vis(hand, 1f))
                    {
                        PrimFx.Charge(hand, (0.4f + 2.2f * k) * _k, FxLayer.Top, 25, false, Primordials.Prism(h == _ha ? 0f : 0.5f));
                        ChoreoFx.Motes(hand, _px, 3, 30f * _k, 0.2f, Color.white, Primordials.Prism(), FxLayer.Top);
                    }
                    h.Pose.Nudge = new Vector2(((int)(_t * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f);
                }
                if (Random.value < 0.3f)
                {
                    Primordials.Flash(Fx.WithA(Color.white, 0.12f * k), 0.08f);
                }
                return;
            }
            if (_pt < 6f)
            {
                // two beams meet between them and push; one gives way
                float k = Mathf.Clamp01((_pt - 2.6f) / 3.4f);
                float push = Mathf.Sin(_pt * 7f) * 0.12f * (1f - k) + Anim.InQuad(k) * 0.48f;
                Vector2 meet = Vector2.Lerp((bw + bl) * 0.5f, bl, push);
                Beam(bw, meet, 5, 0.05f);
                Beam(bl, meet, 3 + (int)(2f * (1f - k)), 0.05f);
                if (Vis(meet, 1f))
                {
                    Spec.Blob(meet, (2f + 2f * k) * _k, _px, 0.06f, FxLayer.Top, 26, 1f, 0.4f, Color.white, Fx.WithA(Primordials.Prism(), 0.7f));
                    DuelFx.Starburst(meet, _px, (1.5f + 2f * k) * _k, Color.white, Primordials.Prism(), Primordials.Prism(0.5f), 0.06f);
                }
                if (Random.value < 0.35f && Settings.Destruction > 0)
                {
                    Kit.Ravage(Combat.Clamp(new Vector2(meet.x, meet.y - Air)), 4f, 3, null, false);
                }
                l.Pose.Nudge = new Vector2(((int)(_t * 70f) & 1) == 0 ? 0.8f : -0.8f, 0f);
                return;
            }
            if (Enter())
            {
                // the loser is unmade
                Beam(bw, bl, 9, 0.5f);
                Burst(bl, 30f * _k, true);
                _waveAt = lo.current_position;
                if (Settings.Destruction > 0)
                {
                    WorldTile t = Combat.TileAt(Combat.Clamp(_waveAt));
                    if (t != null)
                    {
                        MapAction.damageWorld(t, Big(70), TerraformLibrary.czar_bomba, wa);
                    }
                }
                l.Let();
                Primordials.Smite(wa, lo, 50f, true);
                if (lo.isAlive())
                {
                    lo.dieAndDestroy(AttackType.Other);
                }
                if (Dev.On) Dev.Trace("world's end: " + lo.asset.id + "#" + lo.Sid() + " is unmade; the wave runs over the world");
                Kit.Stop(bl, 0.4f);
                Next(Phase.Extinction);
                _waveR = _waveDone = 0f;
            }
        }

        // --------------------------------------------------------------------------- extinction --

        private void Extinction(float dt)
        {
            Hold w = _aWins ? _ha : _hb;
            Actor wa = _aWins ? _a : _b;
            if (Enter())
            {
                Primordials.Flash(Fx.WithA(Color.white, 1f), 1.2f);
                Cinema.Title("EXTINCTION", Color.white, 3f, _waveAt + new Vector2(0f, 6f * _k), 1.4f);
                World.world.startShake(6f, 0.01f, 4f);
                // the world splits open: rivers of lava torn out from where it began to the ends of the world
                _rays.Clear();
                int n = Random.Range(7, 11);
                float a0 = Random.value * 360f;
                for (int i = 0; i < n; i++)
                {
                    _rays.Add(Choreo.Rotate(Vector2.right, a0 + i * 360f / n + Random.Range(-12f, 12f)));
                }
                _craterAcc = 0f;
            }
            if (w != null && !w.Finished)
            {
                Hover(w, w.Pose.Ground, Air, _waveAt);
            }
            // the wave runs out over the whole world in about seven seconds
            float far = Mathf.Sqrt(MapBox.width * MapBox.width + MapBox.height * MapBox.height);
            float speed = Mathf.Max(60f, far / 7f);
            _waveR = Mathf.Min(far + 2f, _waveR + speed * dt);
            // (at most so much ground a frame: on the largest worlds the wave slows rather than the game)
            float budget = 9000f;
            float stepTo = _waveR;
            float area = Mathf.PI * (stepTo * stepTo - _waveDone * _waveDone);
            if (area > budget)
            {
                stepTo = Mathf.Sqrt(_waveDone * _waveDone + budget / Mathf.PI);
            }
            long winner = wa != null ? wa.Sid() : -1;
            if (Settings.Destruction > 0)
            {
                Annulus(_waveAt, _waveDone, stepTo, t =>
                {
                    if (t.Type == null)
                    {
                        return;
                    }
                    if (t.hasBuilding() && t.building != null && t.building.isAlive())
                    {
                        t.building.startDestroyBuilding();
                    }
                    if (!t.Type.liquid)
                    {
                        t.setBurned();
                        float r = Random.value;
                        // great blotches of it die outright (the wasteland a nuke leaves); the rest is burnt
                        if (Mathf.PerlinNoise(t.x * 0.045f + _seed, t.y * 0.045f + _seed) > 0.52f)
                        {
                            Scorch(t);
                        }
                        if (r < 0.015f && _fires < 9000f * WorldScale * WorldScale)
                        {
                            if (t.startFire(true))
                            {
                                _fires++;
                            }
                        }
                    }
                });
            }
            if (Settings.Destruction > 0 && stepTo > _waveDone)
            {
                // the lava rivers run out with the wave
                foreach (Vector2 d in _rays)
                {
                    Vector2 a = _waveAt + d * _waveDone, b = _waveAt + d * stepTo;
                    if (!OnMap(a) || (b - a).sqrMagnitude < 0.09f)
                    {
                        continue;
                    }
                    b = Inside(b);
                    Kit.Trench(a, b, 2.4f * WorldScale, 4, null, false);
                    Lava(a, b);
                }
                // and the land it runs over is torn with craters
                _craterAcc += Mathf.PI * (stepTo * stepTo - _waveDone * _waveDone) / 2600f;
                int made = 0;
                while (_craterAcc >= 1f && made < 3)
                {
                    _craterAcc -= 1f;
                    made++;
                    float ang = Random.value * Mathf.PI * 2f;
                    Vector2 p = _waveAt + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(_waveDone, stepTo);
                    if (OnMap(p) && Land(p))
                    {
                        MapAction.damageWorld(Combat.TileAt(p), Random.Range(4, 9), TerraformLibrary.bomb, null);
                    }
                }
                _craterAcc = Mathf.Min(_craterAcc, 6f);
            }
            // everyone the wave has reached
            foreach (Actor u in World.world.units.getSimpleList())
            {
                if (u == null || !u.isAlive() || u.Sid() == winner)
                {
                    continue;
                }
                if ((u.current_position - _waveAt).sqrMagnitude <= stepTo * stepTo)
                {
                    DoomOne(u);
                }
            }
            _waveDone = stepTo;
            // its front, where it can be seen: a wall of white fire and flying earth
            Camera cam = World.world.camera;
            if (cam != null)
            {
                Vector2 cc = cam.transform.position;
                for (int i = 0; i < 10; i++)
                {
                    float ang = Random.Range(0f, Mathf.PI * 2f);
                    Vector2 at = _waveAt + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * _waveDone;
                    if (!Vis(at, 1f))
                    {
                        // try the arc nearest the view
                        Vector2 dir = (cc - _waveAt);
                        if (dir.sqrMagnitude < 1e-4f)
                        {
                            continue;
                        }
                        at = _waveAt + Choreo.Rotate(dir.normalized, Random.Range(-25f, 25f)) * _waveDone;
                        if (!Vis(at, 1f))
                        {
                            continue;
                        }
                    }
                    Spec.Blob(at, Random.Range(1.5f, 3f) * _k, _px, 0.5f, FxLayer.Top, 20, 1f, 0.5f, Color.white, Fx.WithA(new Color(1f, 0.7f, 0.4f, 1f), 0.8f), Fx.WithA(Primordials.Void, 0f))?.Moving((at - _waveAt).normalized * speed * 0.6f);
                    Spec.Rocks(at, _px, 4, 120f, _k);
                }
            }
            if (_waveDone >= far)
            {
                Next(Phase.After);
            }
        }

        private readonly List<Vector2> _rays = new List<Vector2>();
        private float _craterAcc;

        // ------------------------------------------------------------------------------- scars --

        /// <summary>A wound in the world still spreading: a disk of dead land growing ring by ring.</summary>
        private struct Scar
        {
            public Vector2 At;
            public float R, Done, Fire;
        }

        private readonly List<Scar> _scars = new List<Scar>();
        private float _strikeT;
        private float _seed = Random.value * 100f;

        /// <summary>
        /// Everything within 'r' of 'at' dies as land: grass and forest floor to wasteland, every building
        /// and tree down, the ground burnt, fires ('fire' a chance a tile). Ragged at its rim. It spreads over
        /// the next frames (a budget a frame: the game never stalls on it).
        /// </summary>
        private void ScarDisk(Vector2 at, float r, float fire)
        {
            if (Settings.Destruction <= 0 || r < 1f || _scars.Count > 600)
            {
                return;
            }
            _scars.Add(new Scar { At = at, R = r, Fire = fire });
        }

        private void Scars()
        {
            if (_scars.Count == 0)
            {
                return;
            }
            float budget = 3000f;
            for (int i = _scars.Count - 1; i >= 0 && budget > 0f; i--)
            {
                Scar sc = _scars[i];
                float to = Mathf.Min(sc.R, Mathf.Sqrt(sc.Done * sc.Done + budget / Mathf.PI));
                budget -= Mathf.PI * (to * to - sc.Done * sc.Done);
                Vector2 c = sc.At;
                float rr = sc.R, fire = sc.Fire;
                Annulus(c, sc.Done, to, t =>
                {
                    if (t.Type == null)
                    {
                        return;
                    }
                    // ragged: its outer quarter thins out
                    float dx = t.x + 0.5f - c.x, dy = t.y + 0.5f - c.y;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / rr;
                    if (d > 0.75f && Random.value > (1f - d) / 0.25f)
                    {
                        return;
                    }
                    Ruin(t, fire);
                });
                sc.Done = to;
                if (to >= sc.R)
                {
                    _scars.RemoveAt(i);
                }
                else
                {
                    _scars[i] = sc;
                }
            }
        }

        /// <summary>One tile of the world killed: its building or tree down, its ground dead and burnt, maybe on fire.</summary>
        private void Ruin(WorldTile t, float fire)
        {
            if (t.hasBuilding() && t.building != null && t.building.isAlive())
            {
                t.building.startDestroyBuilding();
            }
            if (t.Type.liquid)
            {
                return;
            }
            Scorch(t);
            t.setBurned();
            if (fire > 0f && Random.value < fire && _fires < 12000f * WorldScale * WorldScale && t.startFire(true))
            {
                _fires++;
            }
        }

        /// <summary>The storm over the whole world: the game's own great lightning striking anywhere on land, each strike a dead patch.</summary>
        private void WorldStorm(float dt)
        {
            if (Settings.Destruction <= 0 || _ph >= Phase.Extinction)
            {
                return;
            }
            _strikeT -= dt;
            if (_strikeT > 0f)
            {
                return;
            }
            _strikeT = Random.Range(0.25f, 0.6f) / (WorldScale * WorldScale);
            for (int i = 0; i < 6; i++)
            {
                Vector2 p = new Vector2(Random.Range(4f, MapBox.width - 4f), Random.Range(4f, MapBox.height - 4f));
                if (!Land(p))
                {
                    continue;
                }
                WorldTile t = Combat.TileAt(p);
                if (t == null)
                {
                    continue;
                }
                // (the game's own lightning is a flat white bar: this one forks and glows)
                if (Vis(p, 1f))
                {
                    Vector2 sky = p + new Vector2(Random.Range(-5f, 5f), 26f) * _k;
                    PrimFx.Bolt(sky, p, 4f * _px, 0.25f, Random.value < 0.5f ? new Color(1f, 0.3f, 0.25f, 1f) : Primordials.Prism(Random.value), FxLayer.Top, 24, false);
                    Kit.Flash(p, _px, 9, 0.1f, Color.white);
                }
                Kit.Ravage(p, 2.5f, 2, null, true);
                DoomAround(p, 3f);
                Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionLightningStrike", t);
                ScarDisk(p, Random.Range(3f, 7f), 0.25f);
                break;
            }
        }

        private static bool OnMap(Vector2 p)
        {
            return p.x >= 1f && p.y >= 1f && p.x < MapBox.width - 1 && p.y < MapBox.height - 1;
        }

        /// <summary>Dead ground: grass, forest floor and sand turned to the grey wasteland a nuke leaves (mountains stay rock).</summary>
        private static void Scorch(WorldTile t)
        {
            if (t.top_type != null && t.top_type.wasteland)
            {
                return;
            }
            if (t.isTileRank(TileRank.Low))
            {
                MapAction.terraformTop(t, TopTileLibrary.wasteland_low, TerraformLibrary.nothing);
            }
            else if (t.isTileRank(TileRank.High))
            {
                MapAction.terraformTop(t, TopTileLibrary.wasteland_high, TerraformLibrary.nothing);
            }
        }

        // -------------------------------------------------------------------------------- after --

        private bool After()
        {
            Hold w = _aWins ? _ha : _hb;
            Actor wa = _aWins ? _a : _b;
            if (!Alive(wa, _aWins ? _idA : _idB))
            {
                wa = null;   // (gone some other way: its body is not to be read)
            }
            if (Enter())
            {
                Cinema.Undim(4f);
                if (wa != null && wa.isAlive())
                {
                    Cinema.Title("THE WORLD HAS ENDED", new Color(0.85f, 0.85f, 0.9f, 1f), 4f, wa.current_position + new Vector2(0f, 5f * _k), 1f);
                }
                if (Dev.On) Dev.Trace("world's end: the world has ended (" + (wa != null ? wa.asset.id + "#" + wa.Sid() : "nobody") + " remains)");
            }
            // the winner comes down alone into the dead world
            if (w != null && !w.Finished)
            {
                w.Pose.Height = Mathf.Max(0f, w.Pose.Height - 1.2f * _k * Time.deltaTime);
                w.Pose.HideShadow = w.Pose.Height > 0.05f;
            }
            if (_pt >= 4f)
            {
                Stop("ended");
                return false;
            }
            return true;
        }

        // ---------------------------------------------------------------------------- geometry --

        /// <summary>Every tile whose centre lies between 'r0' and 'r1' tiles of 'c' (an expanding ring), once.</summary>
        private static void Annulus(Vector2 c, float r0, float r1, System.Action<WorldTile> f)
        {
            if (r1 <= r0)
            {
                return;
            }
            int R = Mathf.CeilToInt(r1);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(c.x) - R), x1 = Mathf.Min(MapBox.width - 1, Mathf.CeilToInt(c.x) + R);
            float r02 = r0 * r0, r12 = r1 * r1;
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - c.x;
                float dx2 = dx * dx;
                if (dx2 > r12)
                {
                    continue;
                }
                float outer = Mathf.Sqrt(r12 - dx2);
                float inner = dx2 < r02 ? Mathf.Sqrt(r02 - dx2) : -1f;
                Rows(x, c.y - outer, inner < 0f ? c.y + outer : c.y - inner, f);
                if (inner >= 0f)
                {
                    Rows(x, c.y + inner, c.y + outer, f);
                }
            }
        }

        private static void Rows(int x, float ya, float yb, System.Action<WorldTile> f)
        {
            int y0 = Mathf.Max(0, Mathf.CeilToInt(ya - 0.5f)), y1 = Mathf.Min(MapBox.height - 1, Mathf.FloorToInt(yb - 0.5f));
            for (int y = y0; y <= y1; y++)
            {
                WorldTile t = World.world.GetTileSimple(x, y);
                if (t != null)
                {
                    f(t);
                }
            }
        }
    }
}
