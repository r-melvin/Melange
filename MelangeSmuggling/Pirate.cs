using System;
using System.Collections.Generic;
using Il2CppScheduleOne.AvatarFramework;
using Il2CppScheduleOne.Core.Avatar;
using UnityEngine;
using Avatar = Il2CppScheduleOne.AvatarFramework.Avatar;

namespace Melange.Smuggling
{
    /// <summary>
    /// Dafydd's pirate look: a tricorn and an eyepatch (scripts/art/build_pirate.py) parented to his head bone at runtime. The
    /// black cowboy hat stays in his appearance as the fallback; once the tricorn is on, the cowboy hat's renderers are hidden
    /// (the accessory itself stays, so whatever it does to his hair still applies). If the head, the models or a material
    /// can't be had, or the cowboy hat can't be found to hide, he keeps the cowboy hat. Purely local looks: every peer
    /// dresses its own copy of him.
    /// </summary>
    internal sealed class Pirate
    {
        private const string HatResource = "MelangeSmuggling.tricorn.mesh.json", PatchResource = "MelangeSmuggling.eyepatch.mesh.json";
        private const string HatName = "MelangeSmuggling_Tricorn", PatchName = "MelangeSmuggling_Eyepatch";
        private const int MaxTries = 30;

        private GameObject _hat, _patch;
        private int _tries;
        private bool _gaveUp, _logged, _hidCowboy;

        /// <summary>Puts the tricorn and eyepatch on if they aren't, and keeps the cowboy hat hidden under the tricorn.</summary>
        public void Dress(GameObject npc)
        {
            if (npc == null) return;
            if (!Settings.PirateLook) { Undress(npc); return; }
            if (_hat != null) _hidCowboy |= HideCowboyHat(npc, true);   // again each time: the game may re-apply his accessories
            if (_gaveUp || (_hat != null && _patch != null)) return;
            try
            {
                if (TryFit(npc, out var fit)) Attach(npc, fit);
                else if (++_tries >= MaxTries)
                {
                    _gaveUp = true;
                    Mod.Log.Warning("Dafydd's pirate look: no head to put it on; he keeps the cowboy hat");
                }
            }
            catch (Exception e)
            {
                _gaveUp = true;
                Mod.Log.Warning("Dafydd's pirate look failed (" + e.Message + "); he keeps the cowboy hat");
                Undress(npc);
            }
        }

        private void Undress(GameObject npc)
        {
            if (_hat != null) UnityEngine.Object.Destroy(_hat);
            if (_patch != null) UnityEngine.Object.Destroy(_patch);
            _hat = _patch = null;
            if (_hidCowboy) HideCowboyHat(npc, false);
            _hidCowboy = false;
        }

        private struct Fit
        {
            public Transform Head;
            public Vector3 Between;            // the point between his eyes, world
            public Quaternion Facing;          // his facing frame: level, forward out of his face
            public float Spacing, EyeRadius;
            public bool FromEyes;
        }

        private static bool TryFit(GameObject npc, out Fit fit)
        {
            fit = default;
            var avatar = npc.GetComponentInChildren<Avatar>(true);
            var head = HeadBone(avatar, npc);
            if (head == null) return false;
            fit.Head = head;
            var root = avatar != null ? avatar.transform : npc.transform;
            Vector3 forward = Flat(root.forward);
            Eye left = null, right = null;
            try { left = avatar?.Eyes?.LeftEye; right = avatar?.Eyes?.RightEye; } catch { }
            if (EyeCentre(left, out var l, out float lr) && EyeCentre(right, out var r, out float rr) && (r - l).magnitude > 0.01f)
            {
                var across = Flat(r - l);
                var fromEyes = Vector3.Cross(across, Vector3.up).normalized;
                if (Vector3.Dot(fromEyes, forward) < 0f) fromEyes = -fromEyes;   // LeftEye may be the viewer's left
                forward = fromEyes;
                fit.Between = (l + r) * 0.5f;
                fit.Spacing = (r - l).magnitude;
                fit.EyeRadius = Mathf.Clamp((lr + rr) * 0.5f, 0.003f, 0.04f);
                fit.FromEyes = true;
            }
            else
            {
                fit.Between = head.position + Vector3.up * (float)PirateFit.EyesAboveHeadBone + forward * (float)PirateFit.EyesBeforeHeadBone;
                fit.Spacing = (float)PirateFit.ModelEyeSpacing;
                fit.EyeRadius = 0.012f;
            }
            fit.Facing = Quaternion.LookRotation(forward, Vector3.up);
            return true;
        }

        private void Attach(GameObject npc, Fit fit)
        {
            var cowboy = CowboyHatMaterial(npc);
            float k = (float)PirateFit.Scale(fit.Spacing);
            if (_hat == null)
            {
                var hatTemplate = Models.Tintable(cowboy) ? cowboy : null;   // a copy of his own hat's material, else a lit one
                _hat = Models.Build(HatResource, fit.Head, HatName, hatTemplate);
                if (_hat != null)
                {
                    float s = (float)PirateFit.HatUp * fit.Spacing, b = (float)PirateFit.HatBack * fit.Spacing;
                    Place(_hat.transform, fit, new Vector3(0f, s, -b) + Offset(Settings.PirateHatOffset), k * (float)Settings.PirateHatScale);
                    _hidCowboy = HideCowboyHat(npc, true);
                    if (!_hidCowboy)
                    {
                        UnityEngine.Object.Destroy(_hat);
                        _hat = null;
                        _gaveUp = true;
                        Mod.Log.Warning("Dafydd's tricorn: the cowboy hat wasn't found to hide; he keeps the cowboy hat");
                    }
                }
                else { _gaveUp = true; Mod.Log.Warning($"Dafydd's tricorn: model {(Models.Has(HatResource) ? "has no material" : "missing")}; he keeps the cowboy hat"); }
            }
            if (_patch == null)
            {
                _patch = Models.Build(PatchResource, fit.Head, PatchName, Models.Tintable(cowboy) ? cowboy : null);
                if (_patch != null) Place(_patch.transform, fit, new Vector3(0f, 0f, fit.EyeRadius) + Offset(Settings.PirateEyepatchOffset), k);
                else { _gaveUp = true; Mod.Log.Warning("Dafydd's eyepatch: model or material missing"); }
            }
            if (!_logged)
            {
                _logged = true;
                Mod.Log.Msg($"Dafydd dressed: head bone '{fit.Head.name}', fitted {(fit.FromEyes ? "to his eyes" : "by guess (no eyes found)")}, " +
                            $"eye spacing {fit.Spacing:0.000} m (scale {k:0.00}), tricorn {(_hat != null ? "on" : "off")}, eyepatch {(_patch != null ? "on" : "off")}" +
                            CowboyBounds(npc));
            }
        }

        /// <summary>Puts a model at an offset in his facing frame from the point between his eyes, at a world scale.</summary>
        private static void Place(Transform t, Fit fit, Vector3 offset, float scale)
        {
            t.SetPositionAndRotation(fit.Between + fit.Facing * offset, fit.Facing);
            var ls = fit.Head.lossyScale;
            t.localScale = new Vector3(scale / NonZero(ls.x), scale / NonZero(ls.y), scale / NonZero(ls.z));
        }

        private static float NonZero(float f) => Mathf.Abs(f) < 1e-4f ? 1f : f;
        private static Vector3 Offset((double X, double Y, double Z) o) => new Vector3((float)o.X, (float)o.Y, (float)o.Z);

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-8f ? Vector3.forward : v.normalized;
        }

        /// <summary>The head bone from the avatar's humanoid animator, else a transform named Head.</summary>
        private static Transform HeadBone(Avatar avatar, GameObject npc)
        {
            try
            {
                Animator animator = avatar?.Animation?._animator;
                if (animator == null) animator = npc.GetComponentInChildren<Animator>(true);
                if (animator != null && animator.isHuman)
                {
                    var bone = animator.GetBoneTransform(HumanBodyBones.Head);
                    if (bone != null) return bone;
                }
            }
            catch { }
            foreach (var t in npc.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name ?? "";
                if (n.Equals("Head", StringComparison.OrdinalIgnoreCase) || n.EndsWith(":Head", StringComparison.OrdinalIgnoreCase)) return t;
            }
            return null;
        }

        private static bool EyeCentre(Eye eye, out Vector3 centre, out float radius)
        {
            centre = default; radius = 0f;
            if (eye == null) return false;
            var ball = eye._eyeballRenderer;
            if (ball != null)
            {
                var b = ball.bounds;
                centre = b.center;
                radius = (b.extents.x + b.extents.y + b.extents.z) / 3f;
                return true;
            }
            var c = eye._container;
            centre = c != null ? c.position : eye.transform.position;
            radius = 0.012f;
            return true;
        }

        // ---- the cowboy hat ----

        private static bool IsCowboy(Accessory a)
        {
            if (a == null) return false;
            string path = null, name = null;
            try { path = a.AssetPath; name = a.Name; } catch { }
            return Has(path, "Cowboy") || Has(name, "Cowboy") || Has(a.gameObject.name, "Cowboy");
        }

        /// <summary>
        /// The cowboy hat's game objects. Since 0.4.7 an avatar wears <c>AvatarObject</c>s (S1API's accessory path maps to one;
        /// in game it is <c>cowboyhat</c>), not <c>Accessory</c> components, and the object's meshes are on its attachments,
        /// which it re-parents to the skeleton. The old component is still looked for.
        /// </summary>
        private static List<GameObject> CowboyHats(GameObject npc)
        {
            var found = new List<GameObject>();
            foreach (var a in npc.GetComponentsInChildren<Accessory>(true))
                if (IsCowboy(a)) found.Add(a.gameObject);
            foreach (var o in npc.GetComponentsInChildren<AvatarObject>(true))
            {
                if (o == null || found.Contains(o.gameObject)) continue;
                string id = null;
                try { id = o.Id; } catch { }
                if (Has(id, "cowboy") || Has(o.gameObject.name, "cowboy")) found.Add(o.gameObject);
            }
            foreach (var at in npc.GetComponentsInChildren<AvatarAttachment>(true))
            {
                AvatarObject parent = null;
                try { parent = at?.Parent; } catch { }
                if (parent != null && found.Contains(parent.gameObject) && !found.Contains(at.gameObject)) found.Add(at.gameObject);
            }
            return found;
        }

        private static List<string> WornNames(GameObject npc)
        {
            var names = new List<string>();
            foreach (var o in npc.GetComponentsInChildren<AvatarObject>(true))
            {
                if (o == null) continue;
                string id = null;
                try { id = o.Id; } catch { }
                names.Add((id ?? "?") + "/" + o.gameObject.name);
            }
            return names;
        }

        private static bool Has(string s, string part) => s != null && s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Shows or hides the cowboy hat's renderers; false when there's no cowboy hat to be found.</summary>
        private static bool HideCowboyHat(GameObject npc, bool hide)
        {
            bool found = false;
            foreach (var a in CowboyHats(npc))
            {
                foreach (var r in a.GetComponentsInChildren<Renderer>(true))
                {
                    found = true;
                    if (r.forceRenderingOff != hide) r.forceRenderingOff = hide;   // not enabled: the game's culling owns that
                }
            }
            return found;
        }

        private static Material CowboyHatMaterial(GameObject npc)
        {
            foreach (var a in CowboyHats(npc))
            {
                foreach (var r in a.GetComponentsInChildren<Renderer>(true))
                    if (r.sharedMaterial != null) return r.sharedMaterial;
            }
            return null;
        }

        /// <summary>For tuning the fit in game: where the cowboy hat's renderers say it is.</summary>
        private static string CowboyBounds(GameObject npc)
        {
            foreach (var a in CowboyHats(npc))
            {
                foreach (var r in a.GetComponentsInChildren<Renderer>(true))
                {
                    var b = r.bounds;
                    return $"; cowboy hat bounds centre {b.center}, size {b.size}, shader {(r.sharedMaterial != null && r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "none")}";
                }
            }
            return "; no cowboy hat found among avatar objects [" + string.Join(", ", WornNames(npc)) + "]";
        }
    }
}
