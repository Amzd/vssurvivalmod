﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.ServerMods
{
    public class WorldGenVillageConfig
    {
        [JsonProperty]
        public float ChanceMultiplier;
        [JsonProperty]
        public WorldGenVillage[] VillageTypes;

        BlockLayerConfig blockLayerConfig;

        internal void Init(ICoreServerAPI api)
        {
            IAsset asset = api.Assets.Get("worldgen/rockstrata.json");
            RockStrataConfig rockstrata = asset.ToObject<RockStrataConfig>();

            asset = api.Assets.Get("worldgen/blocklayers.json");
            blockLayerConfig = asset.ToObject<BlockLayerConfig>();
            blockLayerConfig.ResolveBlockIds(api, rockstrata);


            for (int i = 0; i < VillageTypes.Length; i++)
            {
                LCGRandom rand = new LCGRandom(api.World.Seed + i + 512);
                VillageTypes[i].Init(api, blockLayerConfig, rockstrata, rand);
            }
        }
    }
}
