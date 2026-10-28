using System;
using System.Collections.Generic;

namespace FlipRace.Core
{
    /// <summary>A selection from a session-local signature and timing shuffle.</summary>
    public readonly struct BotPlanChoice
    {
        public readonly int SignatureIndex;
        public readonly int PlanIndex;
        public readonly int AttemptSeed;
        /// <summary>Zero-based attempt number for this exact ordinal key.</summary>
        public readonly long SequenceIndex;
        internal BotPlanChoice(int signature, int plan, int seed, long sequence)
        { SignatureIndex=signature; PlanIndex=plan; AttemptSeed=seed; SequenceIndex=sequence; }
    }

    /// <summary>
    /// Deterministic session-local bags. Keys are opaque ordinal identifiers;
    /// each key must always supply the same ordered groups of global plan IDs.
    /// No physics, catalogue, progress persistence or runtime search is involved.
    /// Call from the owning game thread.
    /// </summary>
    public sealed class BotPlanShuffle
    {
        readonly uint sessionSeed;
        readonly Dictionary<string, KeyState> keys = new Dictionary<string, KeyState>(StringComparer.Ordinal);
        public BotPlanShuffle(uint sessionSeed) { this.sessionSeed=sessionSeed; }

        public BotPlanChoice Next(string key, int[][] groups)
        {
            if(string.IsNullOrWhiteSpace(key))throw new ArgumentException("A nonblank ordinal key is required",nameof(key));
            KeyState state;
            if(keys.TryGetValue(key,out state))state.RequireSame(groups);
            else
            {
                int[][] snapshot=ValidateAndClone(groups);
                state=new KeyState(snapshot,KeySeed(sessionSeed,key));
                keys.Add(key,state);
            }
            return state.Next();
        }

        static int[][] ValidateAndClone(int[][] groups)
        {
            if(groups==null)throw new ArgumentNullException(nameof(groups));
            if(groups.Length==0)throw new ArgumentException("At least one signature is required",nameof(groups));
            var seen=new HashSet<int>();var snapshot=new int[groups.Length][];
            for(int i=0;i<groups.Length;i++)
            {
                if(groups[i]==null||groups[i].Length==0)throw new ArgumentException("Every signature requires timing plans",nameof(groups));
                snapshot[i]=(int[])groups[i].Clone();
                foreach(int plan in snapshot[i])
                    if(plan<0||!seen.Add(plan))throw new ArgumentException("Global plan IDs must be nonnegative and unique across all groups",nameof(groups));
            }
            return snapshot;
        }

        static uint Mix(uint value)
        {
            unchecked { value^=value>>16;value*=0x85ebca6bu;value^=value>>13;value*=0xc2b2ae35u;return value^(value>>16); }
        }
        // Explicit UTF-16 byte order avoids framework string-hash randomization.
        static uint KeySeed(uint session,string key)
        {
            unchecked
            {
                uint hash=2166136261u;
                foreach(char c in key){hash=(hash^(byte)c)*16777619u;hash=(hash^(byte)(c>>8))*16777619u;}
                return Mix(hash^Mix(session+0x9e3779b9u));
            }
        }
        sealed class Generator
        {
            uint state;
            public Generator(uint seed){state=seed;}
            public uint Next(){unchecked {state+=0x9e3779b9u;return Mix(state);}}
            public int Below(int count)
            {
                uint bound=(uint)count,threshold=unchecked(0u-bound)%bound,value;
                do{value=Next();}while(value<threshold);
                return (int)(value%bound);
            }
        }
        sealed class Bag
        {
            readonly int[] values;
            int cursor,last=-1;
            public Bag(int[] values){this.values=(int[])values.Clone();cursor=values.Length;}
            public int Draw(Generator random)
            {
                if(cursor==values.Length)
                {
                    for(int i=values.Length-1;i>0;i--){int j=random.Below(i+1),v=values[i];values[i]=values[j];values[j]=v;}
                    if(values.Length>1&&values[0]==last)
                    {int j=1+random.Below(values.Length-1),v=values[0];values[0]=values[j];values[j]=v;}
                    cursor=0;
                }
                last=values[cursor++];return last;
            }
        }
        sealed class KeyState
        {
            readonly int[][] groups;
            readonly Bag signatures;
            readonly Bag[] timings;
            readonly Generator random;
            readonly uint seed;
            long sequence;
            public KeyState(int[][] groups,uint seed)
            {
                this.groups=groups;this.seed=seed;random=new Generator(seed);
                var ids=new int[groups.Length];timings=new Bag[groups.Length];
                for(int i=0;i<groups.Length;i++){ids[i]=i;timings[i]=new Bag(groups[i]);}
                signatures=new Bag(ids);
            }
            public void RequireSame(int[][] supplied)
            {
                if(supplied==null)throw new ArgumentNullException(nameof(supplied));
                if(supplied.Length!=groups.Length)throw new ArgumentException("An existing key cannot change its groups",nameof(supplied));
                for(int i=0;i<groups.Length;i++)
                {
                    if(supplied[i]==null||supplied[i].Length!=groups[i].Length)throw new ArgumentException("An existing key cannot change its groups",nameof(supplied));
                    for(int j=0;j<groups[i].Length;j++)
                        if(supplied[i][j]!=groups[i][j])throw new ArgumentException("An existing key cannot change its groups",nameof(supplied));
                }
            }
            public BotPlanChoice Next()
            {
                if(sequence==long.MaxValue)throw new InvalidOperationException("Attempt sequence exhausted");
                int signature=signatures.Draw(random),plan=timings[signature].Draw(random);
                long current=sequence++;
                uint attempt=unchecked(Mix(seed^Mix((uint)current)^Mix((uint)(current>>32)+0x27d4eb2du)));
                return new BotPlanChoice(signature,plan,unchecked((int)attempt),current);
            }
        }
    }
}