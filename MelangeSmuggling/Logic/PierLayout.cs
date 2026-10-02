using System;
using System.Collections.Generic;

namespace Melange.Smuggling
{
    /// <summary>
    /// The smuggler's pier (model: scripts/art/build_pier.py, which uses the same numbers): a floating timber pontoon at the
    /// water line between the quay wall and the boat, and a timber staircase with handrails down the wall face from a small
    /// landing on the quay top. The pier's own frame: origin on the quay edge (the wall face) at quay-top height, abreast
    /// the berth's midpoint; +x out into the basin, +y up, +z along the quay. The stair's head is at the -z end.
    /// </summary>
    public static class PierLayout
    {
        // ---- the model's dimensions (metres, pier frame) ----
        public const float WallGap = 0.1f;          // the pontoon's inner edge, out from the wall face
        public const float Width = 2.5f;            // the pontoon, across
        public const float Length = 9f;             // the pontoon, along the quay (z -4.5..4.5)
        public const float Drop = 3.65f;            // quay top down to the pontoon's deck (the quay at -2.5 over water at -6.5, 0.35 m freeboard)
        public const float Freeboard = 0.35f;       // the deck above the water
        public const float StairWidth = 1f;         // the stair, against the wall (x WallGap..WallGap+StairWidth), over the pontoon's inner strip
        public const int Risers = 20;
        public const float Tread = 0.28f;
        public const float StairHead = -4.2f;       // z of the top step's edge, where the landing ends
        public const float LandingIn = -0.6f;       // the landing reaches this far back onto the quay top
        public const float LandingDepth = 1f;       // along the quay, z StairHead-1..StairHead
        public const float LandingTop = 0.03f;      // a plank's thickness above the quay top
        public const float RailHeight = 1f;
        public const float GuardHeight = 0.5f;      // invisible kerb guard round the pontoon's open edges
        public const float BoatGap = 0.25f;         // fenders between the pontoon and the hull
        public const float BoatHalfBeam = 1.15f;    // the speedboat's (build_speedboat.py B / 2)
        public const float FallbackEdge = 1.2f;     // the wall face out from the bollard line when it can't be found

        public static float Riser => Drop / Risers;
        public static float SlopeDegrees => (float)(Math.Atan2(Riser, Tread) * 180.0 / Math.PI);
        /// <summary>Where the stair's last riser meets the deck (z).</summary>
        public static float StairFoot => StairHead + Risers * Tread;
        /// <summary>The pontoon's outer (boat-side) edge.</summary>
        public static float Outer => WallGap + Width;
        /// <summary>The boat's centre line, out from the wall face.</summary>
        public static float BoatOut => Outer + BoatGap + BoatHalfBeam;

        /// <summary>A box collider in the pier's frame: centre, size, and pitch about +x (degrees; positive tips +z down).</summary>
        public sealed class Box
        {
            public string Name;
            public float CX, CY, CZ, SX, SY, SZ, Pitch;
            public bool Walkable;
            public bool Visible;        // false: an invisible guard with no model behind it

            /// <summary>The top face's centre line, from its -z end to its +z end, as (z, y) pairs.</summary>
            public ((float Z, float Y) A, (float Z, float Y) B) TopLine()
            {
                double a = Pitch * Math.PI / 180.0, c = Math.Cos(a), s = Math.Sin(a);
                // local +z -> (z, y) = (c, -s); local +y -> (s, c)
                float tz = CZ + (float)(s * SY / 2), ty = CY + (float)(c * SY / 2);
                float hz = (float)(c * SZ / 2), hy = (float)(-s * SZ / 2);
                return ((tz - hz, ty - hy), (tz + hz, ty + hy));
            }
        }

        /// <summary>
        /// The colliders: the deck, the landing and the stair as walkable surfaces, the handrails, and low invisible guards
        /// on the pontoon's open edges so the player doesn't walk off into the basin. The stair is one smooth ramp (thick, so
        /// nobody walks under it) through the middle of each riser, at <see cref="SlopeDegrees"/>.
        /// </summary>
        public static List<Box> Boxes()
        {
            var list = new List<Box>();
            float deck = -Drop, x0 = WallGap, x1 = WallGap + StairWidth, xm = (x0 + x1) / 2f;
            list.Add(new Box { Name = "deck", CX = WallGap + Width / 2f, CY = deck - 0.2f, CZ = 0f, SX = Width, SY = 0.4f, SZ = Length, Walkable = true, Visible = true });
            list.Add(new Box { Name = "landing", CX = (LandingIn + x1) / 2f, CY = LandingTop - 0.06f, CZ = StairHead - LandingDepth / 2f,
                               SX = x1 - LandingIn, SY = 0.12f, SZ = LandingDepth, Walkable = true, Visible = true });

            // the ramp: its top surface runs from (StairHead, -Riser/2) down to the deck, then 0.3 m on into it
            double th = Math.Atan2(Riser, Tread), c = Math.Cos(th), s = Math.Sin(th);
            float pitch = (float)(th * 180.0 / Math.PI);
            float run = (Risers - 0.5f) * Tread;                    // horizontal, to where the surface meets the deck
            float len = (float)(run / c) + 0.3f;
            float thick = 3f;
            float z0 = StairHead, y0 = -Riser / 2f;
            float midZ = z0 + (float)(c * len / 2), midY = y0 - (float)(s * len / 2);
            list.Add(new Box { Name = "stair", CX = xm, CY = midY - (float)(c * thick / 2), CZ = midZ - (float)(s * thick / 2),
                               SX = StairWidth, SY = thick, SZ = len, Pitch = pitch, Walkable = true, Visible = true });

            // handrails both sides of the stair, from the ramp surface up RailHeight (perpendicular), the stair's length
            float rlen = (float)(run / c);
            float rz = z0 + (float)(c * rlen / 2), ry = y0 - (float)(s * rlen / 2);
            foreach (var (name, x) in new[] { ("rail_wall", x0 - 0.04f), ("rail_open", x1 + 0.04f) })
                list.Add(new Box { Name = name, CX = x, CY = ry + (float)(c * RailHeight / 2), CZ = rz + (float)(s * RailHeight / 2),
                                   SX = 0.08f, SY = RailHeight, SZ = rlen, Pitch = pitch, Visible = true });
            // the landing's rails: across its far end (from its corner post on the quay, x -0.3) and along its open side
            list.Add(new Box { Name = "rail_landing_end", CX = (-0.35f + x1 + 0.08f) / 2f, CY = LandingTop + RailHeight / 2f, CZ = StairHead - LandingDepth + 0.04f,
                               SX = x1 + 0.43f, SY = RailHeight, SZ = 0.08f, Visible = true });
            list.Add(new Box { Name = "rail_landing_side", CX = x1 + 0.04f, CY = LandingTop + RailHeight / 2f, CZ = StairHead - LandingDepth / 2f,
                               SX = 0.08f, SY = RailHeight, SZ = LandingDepth, Visible = true });

            // guards on the pontoon (invisible along the boat side, where the boat comes alongside; the ends have a visible rail)
            float gy = deck + GuardHeight / 2f;
            list.Add(new Box { Name = "guard_boat_side", CX = Outer - 0.05f, CY = gy, CZ = 0f, SX = 0.1f, SY = GuardHeight, SZ = Length });
            list.Add(new Box { Name = "guard_wall_side", CX = WallGap + 0.05f, CY = gy, CZ = 0f, SX = 0.1f, SY = GuardHeight, SZ = Length });
            list.Add(new Box { Name = "rail_far_end", CX = WallGap + Width / 2f, CY = deck + RailHeight / 2f, CZ = Length / 2f - 0.05f,
                               SX = Width, SY = RailHeight, SZ = 0.1f, Visible = true });
            list.Add(new Box { Name = "rail_near_end", CX = WallGap + Width / 2f, CY = deck + RailHeight / 2f, CZ = -Length / 2f + 0.05f,
                               SX = Width, SY = RailHeight, SZ = 0.1f, Visible = true });
            return list;
        }

        /// <summary>Where the pier, the boat and Dafydd go in the world.</summary>
        public sealed class Placement
        {
            /// <summary>The pier's origin (wall face, quay top) and its yaw (degrees, Unity: +z of the frame = (sin, cos)).</summary>
            public float X, Y, Z, Yaw;
            public float BoatX, BoatY, BoatZ, BoatYaw;
            public float DeckY;
            /// <summary>The frame's +x (out into the basin) and +z (along the quay) on the ground plane.</summary>
            public float RightX, RightZ, ForwardX, ForwardZ;

            public (float X, float Y, float Z) World(float lx, float ly, float lz)
                => (X + RightX * lx + ForwardX * lz, Y + ly, Z + RightZ * lx + ForwardZ * lz);
        }

        /// <summary>
        /// The pier's frame for a mooring: +x is the basin's normal, so +z is whichever way along the quay keeps the frame
        /// right-handed in Unity's sense (right = (fz, -fx)).
        /// </summary>
        public static (float FX, float FZ) Forward(Quay.Mooring m)
        {
            float fx = m.AlongX, fz = m.AlongZ;
            if (fz * m.OutX - fx * m.OutZ < 0f) { fx = -fx; fz = -fz; }
            return (fx, fz);
        }

        /// <summary>
        /// The pier at a mooring whose quay wall face is <paramref name="edge"/> metres out from the bollard line, with the
        /// quay top at <paramref name="quayTop"/> and the water at <paramref name="water"/>: the boat lies alongside the
        /// pontoon at the waterline, bow the way the mooring says (towards the next bollard).
        /// </summary>
        public static Placement Place(Quay.Mooring m, float edge, float quayTop, float water)
        {
            var (fx, fz) = Forward(m);
            var p = new Placement
            {
                X = m.MidX + m.OutX * edge, Y = quayTop, Z = m.MidZ + m.OutZ * edge,
                Yaw = (float)(Math.Atan2(fx, fz) * 180.0 / Math.PI),
                RightX = m.OutX, RightZ = m.OutZ, ForwardX = fx, ForwardZ = fz,
                DeckY = quayTop - Drop,
                BoatYaw = m.Yaw, BoatY = water,
            };
            var b = p.World(BoatOut, 0f, 0f);
            p.BoatX = b.X; p.BoatZ = b.Z;
            return p;
        }

        /// <summary>
        /// Dafydd's spot: on the quay by the stair head, as far inland as the mooring puts him. It doesn't depend on where the
        /// wall face is (his spawn point is fixed before the scene can be measured), and stays on the quay, where the navmesh is.
        /// </summary>
        public static (float X, float Z) Stand(Quay.Mooring m)
        {
            var (fx, fz) = Forward(m);
            float along = StairHead - 0.5f;
            return (m.StandX + fx * along, m.StandZ + fz * along);
        }

        /// <summary>
        /// The wall face from ground heights sampled outward from the bollard line every <paramref name="step"/> metres
        /// (null = nothing hit): halfway between the last sample still on the quay and the first more than
        /// <paramref name="drop"/> below its top. Null when the first sample is already off the quay or none is.
        /// </summary>
        public static float? EdgeFromProfile(IList<float?> heights, float step, float quayTop, float drop = 1f)
        {
            if (heights == null || heights.Count == 0) return null;
            bool On(float? h) => h.HasValue && h.Value > quayTop - drop;
            if (!On(heights[0])) return null;
            for (int i = 1; i < heights.Count; i++)
                if (!On(heights[i])) return (i - 0.5f) * step;
            return null;
        }

        /// <summary>A usable edge: a sane measured or configured one, else the fallback.</summary>
        public static float Edge(float? configured, float? measured)
        {
            if (configured.HasValue && configured.Value >= 0f && configured.Value <= 6f) return configured.Value;
            if (measured.HasValue && measured.Value >= 0.2f && measured.Value <= 4f) return measured.Value;
            return FallbackEdge;
        }
    }
}
