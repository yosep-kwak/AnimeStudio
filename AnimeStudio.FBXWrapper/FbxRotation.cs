using System;
using System.Collections.Generic;
using System.Linq;

namespace AnimeStudio
{
    public static partial class Fbx
    {
        public static Quaternion UnityEulerToExportQuaternion(Vector3 value)
        {
            var x=value.X*Math.PI/360;var y=value.Y*Math.PI/360;var z=value.Z*Math.PI/360;
            var sx=Math.Sin(x);var cx=Math.Cos(x);var sy=Math.Sin(y);var cy=Math.Cos(y);var sz=Math.Sin(z);var cz=Math.Cos(z);
            // Unity qY*qX*qZ followed by the exporter's MirrorX basis.
            return new Quaternion((float)(cy*sx*cz+sy*cx*sz),(float)(cy*sx*sz-sy*cx*cz),
                (float)(sy*sx*cz-cy*cx*sz),(float)(cy*cx*cz+sy*sx*sz));
        }

        // Equivalent XYZ solutions must stay on one continuous branch, including across
        // +/-90 degree pitch. Per-axis wrapping alone cannot resolve that ambiguity.
        public static Vector3 ContinuousEuler(Vector3 value, Vector3 previous)
        {
            float Near(float angle, float reference) => angle + 360f * (float)Math.Round((reference - angle) / 360f);
            Vector3 NearAll(Vector3 angles) => new Vector3(Near(angles.X, previous.X), Near(angles.Y, previous.Y), Near(angles.Z, previous.Z));
            double Distance(Vector3 a) => Math.Pow(a.X - previous.X, 2) + Math.Pow(a.Y - previous.Y, 2) + Math.Pow(a.Z - previous.Z, 2);
            var first = NearAll(value);
            var second = NearAll(new Vector3(value.X + 180f, 180f - value.Y, value.Z + 180f));
            return Distance(first) <= Distance(second) ? first : second;
        }

        // Adapt only rotations to FBX's Euler interpolation. Probe all quarter points of
        // each source interval; keep original key times, and bound long intervals too.
        // The error test is a sampling criterion, not a proof about all real-valued times.
        public static List<ImportedKeyframe<Vector3>> SampleRotation(Func<float, Quaternion> evaluate, IEnumerable<float> sourceTimes)
        {
            var times = sourceTimes.Distinct().OrderBy(t => t).ToArray();
            if (times.Length == 0 || times.Any(t => !float.IsFinite(t))) throw new NotSupportedException("Invalid rotation times.");
            Quaternion Q(float time)
            {
                var q = evaluate(time);
                var length = Math.Sqrt((double)q.X*q.X+(double)q.Y*q.Y+(double)q.Z*q.Z+(double)q.W*q.W);
                if (!double.IsFinite(length) || length < 1e-10) throw new NotSupportedException("Invalid sampled rotation.");
                return new Quaternion((float)(q.X/length),(float)(q.Y/length),(float)(q.Z/length),(float)(q.W/length));
            }
            double Error(Quaternion a, Quaternion b)
            {
                var dot = ((double)a.X*b.X+(double)a.Y*b.Y+(double)a.Z*b.Z+(double)a.W*b.W) /
                    Math.Sqrt(((double)a.X*a.X+(double)a.Y*a.Y+(double)a.Z*a.Z+(double)a.W*a.W)*
                              ((double)b.X*b.X+(double)b.Y*b.Y+(double)b.Z*b.Z+(double)b.W*b.W));
                return Math.Acos(Math.Min(1,Math.Abs(dot)))*360/Math.PI;
            }
            var result = new List<ImportedKeyframe<Vector3>> {new ImportedKeyframe<Vector3>(times[0],QuaternionToEuler(Q(times[0])))};
            void Segment(float end, int depth)
            {
                var start = result[result.Count-1];
                var right = ContinuousEuler(QuaternionToEuler(Q(end)),start.value);
                bool divide = end-start.time > 1f/60f;
                for (int k=1;k<=3;k++)
                {
                    var fraction=k/4f;var t=start.time+(end-start.time)*fraction;
                    var e=new Vector3(start.value.X+(right.X-start.value.X)*fraction,start.value.Y+(right.Y-start.value.Y)*fraction,start.value.Z+(right.Z-start.value.Z)*fraction);
                    if (Error(Q(t),EulerToQuaternion(e))>.005) divide=true;
                }
                if (divide)
                {
                    var mid=start.time+(end-start.time)*.5f;
                    if (depth>=20 || mid<=start.time || mid>=end) throw new NotSupportedException("Rotation interpolation exceeds subdivision precision.");
                    Segment(mid,depth+1);Segment(end,depth+1);return;
                }
                if(result.Count>=120000)throw new NotSupportedException("Rotation interpolation exceeds key budget.");
                result.Add(new ImportedKeyframe<Vector3>(end,right));
            }
            foreach(var time in times.Skip(1))Segment(time,0);
            return result;
        }
    }
}
