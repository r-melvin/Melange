using System;
using S1API.Entities;
using S1API.Entities.Appearances.AccessoryFields;
using S1API.Entities.Appearances.BodyLayerFields;
using S1API.Entities.Appearances.CustomizationFields;
using UnityEngine;
using FaceLayers = S1API.Entities.Appearances.FaceLayerFields;

namespace Melange.Psychedelics
{
    /// <summary>
    /// The wildlife officer as a person: an S1API NPC in a park warden's khaki and green who stands at his post by the pond and,
    /// while the toads are out, walks the ring of toad spots on the officer's clock (one lap per
    /// <see cref="WildlifeOfficer.LapMinutes"/>). Catching a toad then checks the spot nearest where he really is. If he never
    /// spawns, or wanders off his round, <see cref="Wild"/> falls back to the clock alone.
    /// </summary>
    /// <remarks>
    /// S1API finds this class itself and builds the prefab before any save is loaded, so the pond's position isn't known
    /// then: he spawns at the <c>PondPosition</c> override if set, else at S1API's example spot, and is warped to his post once
    /// the pond is found. Only the host moves him; the game's networking carries his position to clients, whose catches read
    /// it like the host's. His ID is save data: never rename it.
    /// </remarks>
    public sealed class WildlifeWarden : NPC
    {
        public override bool IsPhysical => true;

        /// <summary>The live NPC this session, once S1API has made it.</summary>
        internal static WildlifeWarden Instance { get; private set; }

        private static readonly Vector3 FallbackSpawn = new Vector3(-50f, 1.06f, 70f);
        /// <summary>He walks this far outside the toad spots, so he doesn't tread on them.</summary>
        private const float WalkOutside = 1.5f, PostOutside = 6f, WarpBeyond = 60f, WarpEvery = 30f;

        private int _target = int.MinValue;          // spot index he was sent to, -1 = his post
        private float _nextWarp;

        internal static void Forget() => Instance = null;

        protected override void ConfigurePrefab(NPCPrefabBuilder b)
        {
            var spawn = Settings.PondOverride(out var pond) ? pond + new Vector3(12f + PostOutside, 0f, 0f) : FallbackSpawn;
            b.WithIdentity(Ids.WardenNpc, "Clive", "Mossop")
             .WithSpawnPosition(spawn)
             .WithAppearanceDefaults(Look)
             .WithRelationshipDefaults(r => r.SetUnlocked(false));
        }

        protected override void OnCreated()
        {
            try
            {
                base.OnCreated();
                Instance = this;
                _target = int.MinValue;
                Appearance.Build();
                // he has no schedule of his own: Wild drives him, and an enabled empty schedule could send him home
                if (Core.Host.IsHost) Schedule.Disable();
            }
            catch (Exception e) { Mod.Log?.Warning("wildlife warden: " + e.Message); }
        }

        protected override void OnDestroyed()
        {
            if (Instance == this) Instance = null;
            base.OnDestroyed();
        }

        /// <summary>A park ranger from the game's wardrobe: khaki shirt and bucket hat, olive trousers and vest, brown boots.</summary>
        private static void Look(NPCPrefabBuilder.AvatarDefaultsBuilder av)
        {
            var khaki = new Color(0.78f, 0.70f, 0.50f);
            var olive = new Color(0.36f, 0.42f, 0.26f);
            var forest = new Color(0.22f, 0.34f, 0.22f);
            var leather = new Color(0.40f, 0.27f, 0.15f);
            av.Gender = 0.1f;
            av.Weight = 0.6f;
            av.Height = 1.0f;
            av.SkinColor = new Color32(200, 150, 115, 255);           // outdoors all day
            av.LeftEyeLidColor = av.SkinColor;
            av.RightEyeLidColor = av.SkinColor;
            av.HairPath = HairStyle.Receding;
            av.HairColor = new Color(0.45f, 0.40f, 0.35f);
            av.EyebrowThickness = 1.2f;
            av.WithFaceLayer(FaceLayers.FacialHair.Stubble, new Color(0.35f, 0.30f, 0.26f));
            av.WithBodyLayer(Shirts.Buttonup, khaki);
            av.WithBodyLayer(Pants.CargoPants, olive);
            av.WithAccessoryLayer(Head.BucketHat, khaki);
            av.WithAccessoryLayer(Chest.OpenVest, forest);
            av.WithAccessoryLayer(Waist.Belt, leather);
            av.WithAccessoryLayer(Feet.CombatBoots, leather);
        }

        /// <summary>Where he is, if he's in the world.</summary>
        internal static bool TryPosition(out Vector3 at)
        {
            at = default;
            var w = Instance;
            try
            {
                if (w?.gameObject == null) return false;
                at = w.Position;
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// The spot he is at, from where he really is: true when he's in the world and near enough the ring to be on his round.
        /// </summary>
        internal static bool TrySpot(Vector3 pond, float pondRadius, int spotCount, out int spot)
        {
            spot = -1;
            if (!TryPosition(out var at)) return false;
            double dx = at.x - pond.x, dz = at.z - pond.z;
            if (!WildlifeOfficer.OnRound(Math.Sqrt(dx * dx + dz * dz), pondRadius)) return false;
            spot = WildlifeOfficer.SpotNearest(dx, dz, spotCount);
            return spot >= 0;
        }

        /// <summary>
        /// Moves him (host only), called by Wild every scan once the pond is known: on the window, towards the spot after the
        /// one the clock puts him at; off it, to his post outside spot 0. Far off (just spawned, or lost), he is warped.
        /// </summary>
        internal static void Drive(Vector3 pond, float pondRadius, int spotCount, int minutesInto, bool open)
        {
            var w = Instance;
            if (w?.gameObject == null || spotCount <= 0 || !Core.Host.IsHost) return;
            int target = open ? WildlifeOfficer.Heading(minutesInto, spotCount) : -1;
            Vector3 point = target < 0 ? Ring(pond, pondRadius + PostOutside, 0, spotCount) : Ring(pond, pondRadius + 0.8f + WalkOutside, target, spotCount);
            try
            {
                var here = w.Position;
                if ((here - point).sqrMagnitude > WarpBeyond * WarpBeyond && Time.unscaledTime >= w._nextWarp)
                {
                    w._nextWarp = Time.unscaledTime + WarpEvery;
                    w.Movement.Warp(point);
                    Mod.Log.Msg($"wildlife warden warped to {(target < 0 ? "his post" : "spot " + target)} at {point}");
                    w._target = int.MinValue;                     // send him on again next scan
                    return;
                }
                if (target == w._target) return;
                w._target = target;
                w.Movement.SetDestination(point);
                if (target < 0) w.Movement.FacePoint(pond);
            }
            catch (Exception e) { Mod.Log.Warning("wildlife warden can't walk: " + e.Message); w._target = target; }
        }

        /// <summary>A speech bubble over him, if he's in the world.</summary>
        internal static void Say(string text)
        {
            try { Instance?.Dialogue.ShowWorldText(text, 5f); } catch { }
        }

        /// <summary>For the probes: where he is, the spot he sees from, where he's headed (and the clock's spot, when given).</summary>
        internal static string Describe(Vector3 pond, float pondRadius, int spotCount, int clockSpot)
        {
            string clock = clockSpot >= 0 ? $", clock says spot {clockSpot}" : "";
            if (!TryPosition(out var at)) return "warden not in the world" + clock;
            var w = Instance;
            string target = w._target == int.MinValue ? "none yet" : w._target < 0 ? "his post" : "spot " + w._target;
            string round = TrySpot(pond, pondRadius, spotCount, out int spot) ? "at spot " + spot : "off the round";
            return $"warden at ({at.x:0.0},{at.y:0.0},{at.z:0.0}) {round}, heading to {target}{clock}";
        }

        private static Vector3 Ring(Vector3 centre, float radius, int spot, int spotCount)
        {
            float a = spot * Mathf.PI * 2f / spotCount;
            return Looks.Ground(centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
        }
    }
}
