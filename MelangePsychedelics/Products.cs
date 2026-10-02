using System;
using System.Collections.Generic;
using S1API.Products;
using S1API.Properties;
using UnityEngine;

namespace Melange.Psychedelics
{
    /// <summary>
    /// Toad and LSD as S1API generic products with compatibility type Shrooms: the only way past the game's hard-coded four
    /// drug families (see docs/research/psychedelics-feasibility.md), and every customer already has a shroom affinity, so
    /// existing shroom buyers want them. Each has its own look (so the game's shroom visual setter, which expects a
    /// ShroomDefinition, is never asked to draw them), a mix map (Shrooms) so the mixing station works, and a save provider
    /// so a fresh process restores them. LSD also has a consumption hook: that's where a tab's batch and design are judged.
    /// </summary>
    internal static class Products
    {
        public static ProductKind ToadKind { get; private set; }
        public static ProductKind LsdKind { get; private set; }
        private static CustomProductDefinition _toad, _lsd;
        private static bool _metadata;
        /// <summary>Mix outputs whose source was LSD: kept off the drying rack too (filled as the game asks for mixes).</summary>
        public static readonly HashSet<string> LsdMixIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public const float ToadPrice = 60f, LsdTabPrice = 25f;

        /// <summary>Once per process: the kinds, the mixing profiles, the consumption hook and the save provider.</summary>
        public static void Start()
        {
            try
            {
                ToadKind = new ProductKindBuilder(Ids.ToadKind).WithCompatibilityDrugType(DrugType.Shrooms).Build();
                LsdKind = new ProductKindBuilder(Ids.LsdKind).WithCompatibilityDrugType(DrugType.Shrooms).Build();
                new ProductMixingProfileBuilder(ToadKind).WithMixerMap(ProductMixingMap.Shrooms).WithPropertyColorMixing()
                    .WithOutputFactory(o => MixOutput(o, ToadKind)).WithOutputFactoryCompatibility("melange.psy:toad-mix", 1).Build();
                new ProductMixingProfileBuilder(LsdKind).WithMixerMap(ProductMixingMap.Shrooms).WithPropertyColorMixing()
                    .WithOutputFactory(o => { LsdMixIds.Add(o.OutputId); return MixOutput(o, LsdKind); })
                    .WithOutputFactoryCompatibility("melange.psy:lsd-mix", 1).Build();
                ProductConsumptionProfileRegistry.RegisterForProductKind(LsdKind, new ProductConsumptionProfileBuilder()
                    .WithProviderCompatibility("melange.psy:lsd-trip", 1)
                    .OnNpcApply(Brands.OnNpcTrip)
                    .Build());
                CustomProductSaveProviderRegistry.Register(new SaveProvider());
                RegisterPresentation();
            }
            catch (Exception e) { Mod.Log.Error("products could not be set up; no Toad or LSD this session: " + e); }
        }

        /// <summary>A mix keeps its family's kind (so LSD mixes are still LSD to the brands) and a fifth over its source's price.</summary>
        private static ProductMixingOutputDefinition MixOutput(ProductMixingOutput o, ProductKind kind)
            => new ProductMixingOutputDefinition(o.MixName, kind, Math.Max(1f, Math.Min(999f, o.SourcePrice * 1.2f)));

        /// <summary>Before each load. The definitions live for the process (S1API re-registers them on a scene change).</summary>
        public static void Register()
        {
            try
            {
                if (ToadKind == null) return;
                _toad ??= BuildOnce(Ids.ToadProduct, ToadBuilder);
                _lsd ??= BuildOnce(Ids.LsdProduct, LsdBuilder);
                Mod.Log.Msg($"products: Toad {(_toad != null ? "ready" : "MISSING")}, LSD {(_lsd != null ? "ready" : "MISSING")}");
            }
            catch (Exception e) { Mod.Log.Error("products could not be registered: " + e); }
        }

        /// <summary>
        /// Builds a product the first time; afterwards (or when the save provider already restored it) the existing one is
        /// fetched, because a second builder claiming the same ID fails by design.
        /// </summary>
        private static CustomProductDefinition BuildOnce(string id, Func<CustomProductDefinitionBuilder> builder)
        {
            if (S1API.Items.ItemManager.GetDefinition(id) is CustomProductDefinition existing) return existing;
            try { return builder()?.Build(); }
            catch (Exception e)
            {
                Mod.Log.Warning($"{id}: build refused ({e.Message}); using the registered one");
                return S1API.Items.ItemManager.GetDefinition(id) as CustomProductDefinition;
            }
        }

        private static CustomProductDefinitionBuilder ToadBuilder()
        {
            var template = Template();
            var baggie = ProductPopulator.GetPackaging("baggie");
            var jar = ProductPopulator.GetPackaging("jar");
            if (template == null || baggie == null || jar == null) { Mod.Log.Warning("Toad: shroom template or baggie/jar packaging unavailable"); return null; }
            return CustomProductItemCreator.CreateBuilder(Ids.ToadProduct, ToadKind)
                .WithName("Toad")
                .WithDescription("Dried toad venom, flaked. Cure it on a drying rack for a better grade.")
                .WithProductPrice(ToadPrice)
                .WithProperties(Property.Disorienting, Property.Euphoric)
                .WithBaseAddictiveness(0.05f)
                .WithValidPackaging(baggie, jar)
                .WithRepresentationsFrom(template)
                .WithEffectDurations(120, 150)
                .WithNativeMixerMap(ProductMixingMap.Shrooms)
                .WithSaveProvider(Ids.ProductSaveProvider, 1, "toad");
        }

        private static CustomProductDefinitionBuilder LsdBuilder()
        {
            var template = Template();
            var baggie = ProductPopulator.GetPackaging("baggie");
            var brick = ProductPopulator.GetPackaging("brick");
            if (template == null || baggie == null || brick == null) { Mod.Log.Warning("LSD: shroom template or baggie/brick packaging unavailable"); return null; }
            return CustomProductItemCreator.CreateBuilder(Ids.LsdProduct, LsdKind)
                .WithName("LSD")
                .WithDescription("Blotter tabs. A full sheet is twenty.")
                .WithProductPrice(LsdTabPrice)
                .WithProperties(Property.ThoughtProvoking, Property.Euphoric)
                .WithBaseAddictiveness(0.02f)
                .WithValidPackaging(baggie, brick)
                .WithRepresentationsFrom(template)
                .WithEffectDurations(240, 360)
                .WithNativeMixerMap(ProductMixingMap.Shrooms)
                .WithSaveProvider(Ids.ProductSaveProvider, 1, "lsd");
        }

        /// <summary>The game's own shroom, as the scaffold for storage footprints and the like (the look is replaced).</summary>
        private static ProductDefinition Template()
        {
            var shroom = Ergot.VanillaShroom();
            return shroom == null ? null : S1API.Items.ItemManager.GetDefinition(shroom.ID) as ProductDefinition;
        }

        /// <summary>Own looks: amber flakes for Toad, a small pale square for a tab, a printed sheet for the brick.</summary>
        private static void RegisterPresentation()
        {
            Func<GameObject> flake = () => Looks.PrefabSource("toad_flake", p => Looks.Box(p, "flake", new Vector3(0.05f, 0.01f, 0.04f), new Color(0.78f, 0.55f, 0.18f)));
            Func<GameObject> tab = () => Looks.PrefabSource("lsd_tab", p => Looks.Box(p, "tab", new Vector3(0.02f, 0.002f, 0.02f), new Color(0.95f, 0.93f, 0.85f)));
            Func<GameObject> sheet = () => Looks.PrefabSource("lsd_sheet", p => Sheet(p));

            ProductPresentationProfileRegistry.RegisterForProduct(Ids.Owner, Ids.ToadProduct, new ProductPresentationProfileBuilder()
                .WithLooseVisual(flake).WithFunctionalProductConvexMeshColliders().WithGeneratedIconFromLooseVisual(256).Build());
            ProductPresentationProfileRegistry.RegisterForProduct(Ids.Owner, Ids.LsdProduct, new ProductPresentationProfileBuilder()
                .WithLooseVisual(tab).WithFunctionalProductConvexMeshColliders().WithGeneratedIconFromLooseVisual(256).Build());

            var few = new[] { P(-0.02f, 0.01f, 0f, 20f), P(0.015f, 0.012f, 0.01f, -35f), P(0f, 0.02f, -0.015f, 70f) };
            ProductPackagingContentProfileRegistry.Register(Ids.Owner, Ids.ToadProduct, "baggie",
                new ProductPackagingContentProfileBuilder().WithContent(flake).AddPlacements(few).Build());
            ProductPackagingContentProfileRegistry.Register(Ids.Owner, Ids.ToadProduct, "jar",
                new ProductPackagingContentProfileBuilder().WithContent(flake).AddPlacements(few).AddPlacement(P(0f, 0.035f, 0f, 10f)).Build());
            ProductPackagingContentProfileRegistry.Register(Ids.Owner, Ids.LsdProduct, "baggie",
                new ProductPackagingContentProfileBuilder().WithContent(tab).AddPlacement(P(0f, 0.01f, 0f, 15f)).Build());
            ProductPackagingContentProfileRegistry.Register(Ids.Owner, Ids.LsdProduct, "brick",
                new ProductPackagingContentProfileBuilder().WithCompleteFilledVisual(sheet).Build());
        }

        private static ProductPresentationTransform P(float x, float y, float z, float yaw)
            => new ProductPresentationTransform(new Vector3(x, y, z), new Vector3(0f, yaw, 0f), Vector3.one);

        /// <summary>A blotter sheet: a pale card with a grid of perforation lines (5 x 4 tabs). The printed design is a later step (TESTING.md).</summary>
        private static GameObject Sheet(Transform parent)
        {
            var root = new GameObject("sheet");
            root.transform.SetParent(parent, false);
            Looks.Box(root.transform, "card", new Vector3(0.15f, 0.003f, 0.12f), new Color(0.96f, 0.94f, 0.88f));
            for (int i = 1; i < 5; i++)
                Looks.Box(root.transform, "perf", new Vector3(0.001f, 0.0035f, 0.12f), new Color(0.7f, 0.68f, 0.62f)).transform.localPosition = new Vector3(-0.075f + i * 0.03f, 0f, 0f);
            for (int j = 1; j < 4; j++)
                Looks.Box(root.transform, "perf", new Vector3(0.15f, 0.0035f, 0.001f), new Color(0.7f, 0.68f, 0.62f)).transform.localPosition = new Vector3(0f, 0f, -0.06f + j * 0.03f);
            return root;
        }

        /// <summary>
        /// After load: the Product Manager sections need an icon, which exists only once the generated one is done, so they are
        /// registered once per process, when it is. (Discovery waits for the player's first venom or sheet: <see cref="DiscoverOnce"/>.)
        /// </summary>
        public static void AfterLoad()
        {
            try
            {
                if (!_metadata && _toad != null && _lsd != null)
                {
                    var toadIcon = Il2CppScheduleOne.Registry.GetItem(Ids.ToadProduct)?.Icon;
                    var lsdIcon = Il2CppScheduleOne.Registry.GetItem(Ids.LsdProduct)?.Icon;
                    if (toadIcon != null && lsdIcon != null)
                    {
                        new ProductKindMetadataBuilder(ToadKind).WithDisplayName("Toad").WithColor(new Color(0.78f, 0.55f, 0.18f))
                            .WithIcon(toadIcon).WithSearchAliases("toad", "venom").WithProductManagerVisibility(true).Build();
                        new ProductKindMetadataBuilder(LsdKind).WithDisplayName("LSD").WithColor(new Color(0.55f, 0.35f, 0.85f))
                            .WithIcon(lsdIcon).WithSearchAliases("lsd", "acid", "tabs", "blotter").WithProductManagerVisibility(true).Build();
                        _metadata = true;
                    }
                }
            }
            catch (Exception e) { Mod.Log.Warning("products after load: " + e.Message); }
        }

        /// <summary>
        /// The first time the player makes a product it is discovered and listed, as the game does for a first cook; never
        /// again, so a product the player unlisted stays unlisted. Host only (the game networks discovery).
        /// </summary>
        public static void DiscoverOnce(string productId)
        {
            try
            {
                if (!Core.Host.IsHost) return;
                var list = Il2CppScheduleOne.Product.ProductManager.DiscoveredProducts;
                for (int i = 0; list != null && i < list.Count; i++) if (list[i]?.ID == productId) return;
                var pm = Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Product.ProductManager>.Instance;
                if (pm == null) return;
                pm.SetProductDiscovered(null, productId, true);
                Mod.Log.Msg($"{productId}: discovered and listed");
            }
            catch (Exception e) { Mod.Log.Warning($"{productId}: not discovered: {e.Message}"); }
        }

        /// <summary>A loose (or packaged) product instance in the game's own type, at a quality tier.</summary>
        public static Il2CppScheduleOne.Product.ProductItemInstance Make(string productId, int quantity, int tier, string packagingId = null)
        {
            var def = Il2CppScheduleOne.Registry.GetItem(productId);
            var inst = def?.GetDefaultInstance(quantity)?.TryCast<Il2CppScheduleOne.Product.ProductItemInstance>();
            if (inst == null) return null;
            inst.SetQuality((Il2CppScheduleOne.ItemFramework.EQuality)Tier.Clamp(tier));
            if (packagingId != null)
            {
                var pack = Il2CppScheduleOne.Registry.GetItem(packagingId)?.TryCast<Il2CppScheduleOne.Product.Packaging.PackagingDefinition>();
                if (pack == null) return null;
                inst.SetPackaging(pack);
            }
            return inst;
        }

        /// <summary>Restores the two products on a fresh process from the save's descriptor (S1API skips this when they're already built).</summary>
        private sealed class SaveProvider : ICustomProductSaveProvider
        {
            public string ProviderId => Ids.ProductSaveProvider;
            public int MaximumDescriptorVersion => 1;
            public CustomProductDefinitionBuilder Restore(CustomProductSaveDescriptor descriptor)
            {
                switch (descriptor?.ProviderData)
                {
                    case "toad": return ToadBuilder();
                    case "lsd": return LsdBuilder();
                    default: return null;
                }
            }
        }
    }
}
