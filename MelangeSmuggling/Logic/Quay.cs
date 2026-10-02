using System;

namespace Melange.Smuggling
{
    /// <summary>
    /// Where the boat ties up, from the Docks quay's mooring bollards (scene positions, docs/research/smuggling-research.md
    /// 2.4): (-87,-27), (-80,-24), (-73,-29), (-67,-39), (-61,-50). The basin lies to the east of that line (the Docks
    /// Warehouse and the fishing hut are on the land side, to the west), so the boat lies on the east side, parallel to
    /// the quay, between two bollards; Dafydd stands on the quay beside it. Positions are (x, z) on the ground plane.
    /// </summary>
    public static class Quay
    {
        public static readonly (float X, float Z)[] Bollards = { (-87f, -27f), (-80f, -24f), (-73f, -29f), (-67f, -39f), (-61f, -50f) };

        /// <summary>The canal mouth (Region_Docks/Sewerage pipe), where the bootleggers' route comes out at the Docks.</summary>
        public static readonly (float X, float Y, float Z) CanalMouth = (-34.5f, -5f, -10.7f);
        /// <summary>The central canal pipe's barred door, where the route leaves the sewer.</summary>
        public static readonly (float X, float Y, float Z) CanalPipe = (26.4f, -4f, 11.3f);

        public sealed class Mooring
        {
            public float BoatX, BoatZ, Yaw;
            public float StandX, StandZ;
            /// <summary>The midpoint between the two bollards, the unit vector along the quay (towards the next bollard) and the basin's normal.</summary>
            public float MidX, MidZ, AlongX, AlongZ, OutX, OutZ;
        }

        /// <summary>
        /// Moors between bollards <paramref name="a"/> and <paramref name="a"/>+1: the boat <paramref name="offshore"/> metres
        /// out from the midpoint, bow along the quay towards the next bollard; Dafydd <paramref name="ashore"/> metres inland.
        /// "Out" is whichever side of the line has the larger x (the basin's side).
        /// </summary>
        public static Mooring Between(int a, float offshore = 3f, float ashore = 1.5f)
        {
            a = Math.Max(0, Math.Min(Bollards.Length - 2, a));
            var p = Bollards[a]; var q = Bollards[a + 1];
            float dx = q.X - p.X, dz = q.Z - p.Z;
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            dx /= len; dz /= len;
            float nx = dz, nz = -dx;                         // one of the two normals
            if (nx < 0f) { nx = -nx; nz = -nz; }             // the basin's side
            float mx = (p.X + q.X) / 2f, mz = (p.Z + q.Z) / 2f;
            return new Mooring
            {
                BoatX = mx + nx * offshore,
                BoatZ = mz + nz * offshore,
                Yaw = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI),
                StandX = mx - nx * ashore,
                StandZ = mz - nz * ashore,
                MidX = mx, MidZ = mz, AlongX = dx, AlongZ = dz, OutX = nx, OutZ = nz,
            };
        }

        /// <summary>The default berth: between the third and fourth bollards, the middle of the quay.</summary>
        public static Mooring Default => Between(2);

        public static float Distance(float ax, float ay, float az, float bx, float by, float bz)
        {
            float x = ax - bx, y = ay - by, z = az - bz;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }

        /// <summary>
        /// On the route: near the canal mouth or the canal pipe, or down in the sewer tunnels (y -9..-4 over x -54..103,
        /// z 1..115, from the scene). The box reaches down to z -12 to take in the canal bed (about y -4.5, z 0) as far as
        /// the canal mouth; the streets around it stand higher (the Docks at about y -2.5), so they don't count.
        /// </summary>
        public static bool OnRoute(float x, float y, float z, float near = 12f)
        {
            if (Distance(x, y, z, CanalMouth.X, CanalMouth.Y, CanalMouth.Z) <= near) return true;
            if (Distance(x, y, z, CanalPipe.X, CanalPipe.Y, CanalPipe.Z) <= near) return true;
            bool inTunnels = y <= -3.5f && y >= -10f && x >= -54f && x <= 103f && z >= -12f && z <= 115f;
            return inTunnels;
        }
    }
}
