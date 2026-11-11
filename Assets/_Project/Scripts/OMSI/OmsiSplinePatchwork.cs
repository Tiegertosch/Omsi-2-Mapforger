using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The <c>[patchwork_chain]</c> of a spline profile: the texture is cut into parts, and the spline is laid with a chain of them, each
    /// part starting with the letter the one before ends with (<c>AAAAAABBACAADDAAA</c> names the transitions A-A, A-A, ... between
    /// the letters). Several parts can follow a letter, drawn by weight; a part may be laid backwards where its flag allows it.
    /// </summary>
    public sealed class OmsiPatchworkChain
    {
        /// <summary>Length of one stretch of the spline, in metres.</summary>
        public double SegmentLength { get; internal set; }

        /// <summary>The letters of the chain: the part k goes from letter k to letter k + 1.</summary>
        public string Chain { get; internal set; }

        /// <summary>The weight (a digit) of each part.</summary>
        public string Weights { get; internal set; }

        /// <summary>For each part, 1 when it may be laid backwards.</summary>
        public string Invertable { get; internal set; }

        /// <summary>
        /// Whether OMSI takes the chain: as many weights and flags as the chain has transitions, the flags only 0 and 1 (Omsi.exe
        /// sub_5ab908, as openOMSI reads it); otherwise the chain is left out.
        /// </summary>
        public bool IsValid
        {
            get
            {
                var n = Chain?.Length ?? 0;
                if (n < 2 || Weights == null || Invertable == null || Weights.Length + 1 != n || Invertable.Length + 1 != n)
                    return false;
                foreach (var flag in Invertable)
                    if (flag != '0' && flag != '1')
                        return false;
                return true;
            }
        }
    }

    /// <summary>
    /// Which part of the texture each stretch of a spline shows, in OMSI's order. Ported from openOMSI (<c>patchwork</c> in
    /// <c>crates/omsi-geometry/src/lib.rs</c>, after Omsi.exe sub_5aee00): the spline is cut into <c>trunc(length / segment length) + 1</c>
    /// stretches; each takes a part whose first letter is the last letter of the part before (or a part laid backwards), drawn by weight
    /// with the map's own random numbers, seeded by the spline's ID plus 100 per stretch and stirred <c>(id + stretch) mod 7 + 1</c>
    /// times by <c>x = (x * 31415926 + 1) mod 10^8</c>. The last stretch has to end on the chain's first letter, so the next spline joins on.
    /// </summary>
    public sealed class OmsiSplinePatchwork
    {
        private OmsiSplinePatchwork()
        {
        }

        /// <summary>The texture slot the chain belongs to.</summary>
        public int Texture { get; private set; }

        /// <summary>Parts the texture is cut into.</summary>
        public int Parts { get; private set; }

        /// <summary>Cross-sections per stretch.</summary>
        public int PerStretch { get; private set; }

        /// <summary>Per stretch the part, from 1; negative when it is laid backwards.</summary>
        public int[] Order { get; private set; }

        /// <summary>Cross-sections of the whole spline: every stretch has the same number.</summary>
        public int Stations => Order.Length * PerStretch;

        /// <summary>
        /// The order for a spline of a profile with a patchwork chain, or null. OMSI keeps one chain per spline type, the last one defined.
        /// <paramref name="minimumStations"/> is how many cross-sections the spline needs for its curve; the stretches get at least that many.
        /// </summary>
        public static OmsiSplinePatchwork For(OmsiSplineProfile profile, OmsiSplineRecord spline, int minimumStations)
        {
            if (profile == null || spline == null)
                return null;
            var texture = -1;
            OmsiPatchworkChain pc = null;
            for (var index = profile.Textures.Count - 1; index >= 0; index--)
                if (profile.Textures[index].Patchwork != null && profile.Textures[index].Patchwork.IsValid)
                {
                    texture = index;
                    pc = profile.Textures[index].Patchwork;
                    break;
                }
            if (pc == null || !(pc.SegmentLength > 0.0) || !(spline.LengthMeters > 0.0) || double.IsInfinity(spline.LengthMeters))
                return null;

            var chain = pc.Chain;
            var parts = pc.Weights.Length;
            Func<int, int> weight = part => Math.Min(9, (int)(byte)(pc.Weights[part] - '0'));
            Func<int, bool> invertible = part => pc.Invertable[part] == '1';
            var stretches = (int)Math.Min(1e6, Math.Max(0.0, spline.LengthMeters / pc.SegmentLength)) + 1;
            var count = Math.Max(1, Math.Max(minimumStations, (int)(spline.LengthMeters / 10.0)));
            var per = Math.Max(1, count / stretches + 1);

            var state = chain[0];
            var order = new int[stretches];
            var seed = (long)spline.Id;
            for (var k = 0; k < stretches; k++)
            {
                var candidates = new List<int>();
                if (k + 1 == stretches)
                {
                    for (var p = 0; p < parts; p++)
                        if (chain[p] == state && chain[p + 1] == chain[0])
                            for (var copy = 0; copy < weight(p); copy++)
                                candidates.Add(p + 1);
                    for (var p = 0; p < parts; p++)
                        if (invertible(p) && chain[p + 1] == state && chain[p] == chain[0])
                            for (var copy = 0; copy < weight(p); copy++)
                                candidates.Add(-(p + 1));
                }
                if (candidates.Count == 0)
                    for (var q = -parts; q <= parts; q++)
                    {
                        var p = Math.Abs(q);
                        var fits = q < 0 ? invertible(p - 1) && chain[p] == state : q > 0 && chain[p - 1] == state;
                        if (fits)
                            for (var copy = 0; copy < weight(p - 1); copy++)
                                candidates.Add(q);
                    }
                var x = seed + 100L * k;
                for (var stir = 0; stir <= (seed + k) % 7; stir++)
                    x = (x * 31415926L + 1L) % 100000000L;
                var r = x / 1e8;
                // A chain that cannot go on: OMSI logs it and takes any part.
                var part = candidates.Count == 0
                    ? Math.Min((int)(r * parts) + 1, parts)
                    : candidates[Math.Min((int)(candidates.Count * r), candidates.Count - 1)];
                state = part < 0 ? chain[-part - 1] : chain[part];
                order[k] = part;
            }
            return new OmsiSplinePatchwork { Texture = texture, Parts = parts, PerStretch = per, Order = order };
        }
    }
}
