using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// What a fighter remembers about one opponent: the kinds of attack it keeps throwing (its
    /// habits: the slip-and-liver is readied against a foe that keeps throwing uppercuts), which of
    /// the fighter's own techniques it answered and which landed, the last techniques used on it
    /// (never the same one three times running), how often it broke the fighter's charges, how often
    /// its feints fooled it. Habits fade (half-life about 25 s); a fighter remembers its last six
    /// opponents. Not saved.
    /// </summary>
    internal sealed class FoeNotes
    {
        public const int Max = 6;
        private const float HalfLife = 25f;

        public long Id;
        public double Seen;

        /// <summary>Per Form bit: how much of that kind of attack it has thrown at us (fading).</summary>
        public readonly float[] Habit = new float[FormSet.Bits];
        private double _habitAt;

        /// <summary>Per Form bit: how often our attacks of that kind were answered by it (fading).</summary>
        public readonly float[] Burned = new float[FormSet.Bits];

        /// <summary>Our last two techniques on it (newest first).</summary>
        public Technique Last1, Last2;

        /// <summary>Times it broke our charges, times its feints fooled us (count a bite with Reads.Fooled).</summary>
        public int Interrupted, Fooled;

        /// <summary>The feint last counted (one feint is one bite).</summary>
        private Move _fooledBy;

        private readonly List<Technique> _tech = new List<Technique>(8);
        private readonly List<float> _used = new List<float>(8), _landed = new List<float>(8), _countered = new List<float>(8);

        /// <summary>The notes a fighter keeps on 'v' (made if 'make'; the oldest of six forgotten).</summary>
        public static FoeNotes Of(Fighter f, Actor v, bool make)
        {
            if (f == null || v == null)
            {
                return null;
            }
            long id = v.Sid();
            if (id < 0)
            {
                return null;
            }
            List<FoeNotes> list = f.Notes;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id == id)
                {
                    if (make)
                    {
                        list[i].Seen = Fighter.Now;
                    }
                    return list[i];
                }
            }
            if (!make)
            {
                return null;
            }
            FoeNotes n;
            if (list.Count >= Max)
            {
                int old = 0;
                for (int i = 1; i < list.Count; i++)
                {
                    if (list[i].Seen < list[old].Seen)
                    {
                        old = i;
                    }
                }
                n = list[old];
                n.Reset(id);
            }
            else
            {
                n = new FoeNotes();
                n.Reset(id);
                list.Add(n);
            }
            return n;
        }

        private void Reset(long id)
        {
            Id = id;
            Seen = Fighter.Now;
            _habitAt = Fighter.Now;
            for (int i = 0; i < FormSet.Bits; i++)
            {
                Habit[i] = 0f;
                Burned[i] = 0f;
            }
            Last1 = Last2 = null;
            Interrupted = Fooled = 0;
            _fooledBy = null;
            _tech.Clear();
            _used.Clear();
            _landed.Clear();
            _countered.Clear();
        }

        /// <summary>Lets the habits fade to now.</summary>
        private void Fade()
        {
            double now = Fighter.Now;
            float dt = (float)(now - _habitAt);
            if (dt < 0.5f)
            {
                return;
            }
            _habitAt = now;
            float k = Mathf.Pow(0.5f, dt / HalfLife);
            for (int i = 0; i < FormSet.Bits; i++)
            {
                Habit[i] *= k;
                Burned[i] *= k;
            }
        }

        private static void Add(float[] into, Form f, float w)
        {
            ulong x = (ulong)f;
            for (int i = 0; i < FormSet.Bits && x != 0; i++)
            {
                ulong b = 1UL << i;
                if ((x & b) != 0)
                {
                    x &= ~b;
                    into[i] = Mathf.Min(into[i] + w, 12f);
                }
            }
        }

        /// <summary>It threw an attack of these forms at us ('w': 1 a technique, about 0.3 a plain blow).</summary>
        public void Saw(Form f, float w)
        {
            Fade();
            Add(Habit, f, w);
            Seen = Fighter.Now;
        }

        /// <summary>It answered one of our attacks of these forms (parried, evaded).</summary>
        public void Answered(Form f, Technique ours)
        {
            Fade();
            Add(Burned, f, 1f);
            if (ours != null)
            {
                int i = Slot(ours);
                _countered[i] += 1f;
            }
        }

        /// <summary>Its feint 'by' fooled us: counted once per feint move ('by' null: always). True if counted.</summary>
        public bool Fool(Move by)
        {
            if (by != null && by == _fooledBy)
            {
                return false;
            }
            _fooledBy = by;
            Fooled++;
            Seen = Fighter.Now;
            return true;
        }

        /// <summary>We started a technique on it.</summary>
        public void Used(Technique t)
        {
            if (t == null)
            {
                return;
            }
            Last2 = Last1;
            Last1 = t;
            _used[Slot(t)] += 1f;
            Seen = Fighter.Now;
        }

        /// <summary>One of our techniques landed on it.</summary>
        public void Landed(Technique t)
        {
            if (t != null)
            {
                _landed[Slot(t)] += 1f;
            }
        }

        private int Slot(Technique t)
        {
            int i = _tech.IndexOf(t);
            if (i >= 0)
            {
                return i;
            }
            if (_tech.Count >= 12)
            {
                // the least used one makes room
                int low = 0;
                for (int k = 1; k < _tech.Count; k++)
                {
                    if (_used[k] < _used[low])
                    {
                        low = k;
                    }
                }
                _tech[low] = t;
                _used[low] = _landed[low] = _countered[low] = 0f;
                return low;
            }
            _tech.Add(t);
            _used.Add(0f);
            _landed.Add(0f);
            _countered.Add(0f);
            return _tech.Count - 1;
        }

        /// <summary>How strongly its habits lean to any of these forms (0 none .. 1 it does little else).</summary>
        public float HabitOf(Form f)
        {
            if (f == Form.None)
            {
                return 0f;
            }
            Fade();
            float sum = 0f, total = 0f;
            for (int i = 0; i < FormSet.Bits; i++)
            {
                total += Habit[i];
                if (((ulong)f & (1UL << i)) != 0)
                {
                    sum = Mathf.Max(sum, Habit[i]);
                }
            }
            if (total < 0.5f)
            {
                return 0f;
            }
            // a habit is a form that keeps coming back: 3 throws of it count as a full habit
            return Mathf.Clamp01(sum / 3f);
        }

        /// <summary>How much our attacks of these forms have been answered by it (0 .. 1).</summary>
        public float BurnedOf(Form f)
        {
            if (f == Form.None)
            {
                return 0f;
            }
            Fade();
            float m = 0f;
            for (int i = 0; i < FormSet.Bits; i++)
            {
                if (((ulong)f & (1UL << i)) != 0)
                {
                    m = Mathf.Max(m, Burned[i]);
                }
            }
            return Mathf.Clamp01(m / 2f);
        }

        /// <summary>Of our uses of 't' on it, the share it answered, and the share that landed.</summary>
        public void Record(Technique t, out float countered, out float landed, out float used)
        {
            int i = _tech.IndexOf(t);
            if (i < 0)
            {
                countered = landed = used = 0f;
                return;
            }
            used = _used[i];
            float n = Mathf.Max(1f, used);
            countered = Mathf.Clamp01(_countered[i] / n);
            landed = Mathf.Clamp01(_landed[i] / n);
        }
    }
}
