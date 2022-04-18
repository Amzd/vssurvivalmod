﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public class CuboidWithMaterial : Cuboidi
    {
        public byte Material;

        public int this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return X1;
                    case 1: return Y1;
                    case 2: return Z1;
                    case 3: return X2;
                    case 4: return Y2;
                    case 5: return Z2;
                }

                throw new ArgumentOutOfRangeException("Must be index 0..5");
            }
        }

        public Cuboidf ToCuboidf()
        {
            return new Cuboidf(X1 / 16f, Y1/ 16f, Z1 / 16f, X2 / 16f, Y2 / 16f, Z2 / 16f);
        }

        public bool ContainsOrTouches(CuboidWithMaterial neib, int axis)
        {
            switch (axis)
            {
                case 0: // X-Axis
                    return neib.Z2 <= Z2 && neib.Z1 >= Z1 && neib.Y2 <= Y2 && neib.Y1 >= Y1;
                case 1: // Y-Axis
                    return neib.X2 <= X2 && neib.X1 >= X1 && neib.Z2 <= Z2 && neib.Z1 >= Z1;
                case 2: // Z-Axis
                    return neib.X2 <= X2 && neib.X1 >= X1 && neib.Y2 <= Y2 && neib.Y1 >= Y1;
            }

            throw new ArgumentOutOfRangeException("axis must be 0, 1 or 2");
        }
    }
    
    public struct Voxel : IEquatable<Voxel>
    {
        public byte x;
        public byte y;
        public byte z;

        public Voxel(byte x, byte y, byte z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public bool Equals(Voxel other)
        {
            return x == other.x && y == other.y && z == other.z;
        }
    }

    

    public class BlockEntityMicroBlock : BlockEntity, IBlockEntityRotatable
    {
        protected static ThreadLocal<CuboidWithMaterial[]> tmpCuboidTL = new ThreadLocal<CuboidWithMaterial[]>(() => {
            var val = new CuboidWithMaterial[16 * 16 * 16];
            for (int i = 0; i < val.Length; i++) val[i] = new CuboidWithMaterial();
            return val;
        });
        protected static CuboidWithMaterial[] tmpCuboids => tmpCuboidTL.Value;

        // bits 0..3 = xmin
        // bits 4..7 = xmax
        // bits 8..11 = ymin
        // bits 12..15 = ymax
        // bits 16..19 = zmin
        // bits 20..23 = zmas
        // bits 24..31 = materialindex
        public List<uint> VoxelCuboids = new List<uint>();

        public int SnowLevel = 0;
        public int PrevSnowLevel = 0;
        public int snowLayerBlockId;
        public List<uint> SnowCuboids = new List<uint>();
        public List<uint> GroundSnowCuboids = new List<uint>();

        /// <summary>
        /// List of block ids for the materials used
        /// </summary>
        public int[] MaterialIds;
        
        
        public MeshData Mesh;
        public MeshData SnowMesh;
        protected Cuboidf[] selectionBoxes = new Cuboidf[0];
        protected Cuboidf[] selectionBoxesVoxels = new Cuboidf[0];
        protected int prevSize = -1;

        public string BlockName { get; set; } = "";

        protected int emitSideAo = 0x3F;
        protected bool absorbAnyLight;
        public bool[] sidecenterSolid = new bool[6];
        public bool[] sideAlmostSolid = new bool[6];

        public byte[] LightHsv
        {
            get
            {
                int[] matids = MaterialIds;
                byte[] hsv = new byte[3];
                int q = 0;

                if (matids == null) return hsv;

                for (int i = 0; i < matids.Length; i++)
                {
                    Block block = Api.World.BlockAccessor.GetBlock(matids[i]);
                    if (block.LightHsv[2] > 0)
                    {
                        hsv[0] += block.LightHsv[0];
                        hsv[1] += block.LightHsv[1];
                        hsv[2] += block.LightHsv[2]; // Should take into account the amount of used voxels, but then we need to pass the old light hsv to the relighting engine or we'll get lighting bugs
                        q++;  
                    }
                }

                if (q == 0) return hsv;

                hsv[0] = (byte)(hsv[0] / q);
                hsv[1] = (byte)(hsv[1] / q);
                hsv[2] = (byte)(hsv[2] / q);


                return hsv;
            }
        }

        protected byte nowmaterialIndex;

        public float sizeRel=1;

        protected int totalVoxels;

        /// <summary>
        /// A value from 0..1 describing how % of the full block is still left
        /// </summary>
        public float VolumeRel => totalVoxels / (16f * 16f * 16f);

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            if (MaterialIds != null)
            {
                //if (api.Side == EnumAppSide.Client) RegenMesh();
                //RegenSelectionBoxes(null);
            }

            SnowLevel = (int)Block.snowLevel;
            snowLayerBlockId = (Block as BlockMicroBlock)?.snowLayerBlockId ?? 0;            
        }

        public BlockSounds GetSounds()
        {
            var mbsounds = (Block as BlockMicroBlock).MBSounds.Value;
            mbsounds.Init(this, Block);
            return mbsounds;
        }

        public int GetLightAbsorption()
        {
            if (MaterialIds == null || !absorbAnyLight || Api == null)
            {
                return 0;
            }

            int absorb = 99;

            for (int i = 0; i < MaterialIds.Length; i++)
            {
                Block block = Api.World.GetBlock(MaterialIds[i]);
                absorb = Math.Min(absorb, block.LightAbsorption);
            }

            return absorb;
        }



        public bool CanAttachBlockAt(BlockFacing blockFace, Cuboidi attachmentArea = null)
        {
            if (attachmentArea == null)
            {
                return sidecenterSolid[blockFace.Index];
            } else
            {
                HashSet<XYZ> req = new HashSet<XYZ>();
                for (int x = attachmentArea.X1; x <= attachmentArea.X2; x++)
                {
                    for (int y = attachmentArea.Y1; y <= attachmentArea.Y2; y++)
                    {
                        for (int z = attachmentArea.Z1; z <= attachmentArea.Z2; z++)
                        {
                            XYZ vec;

                            switch (blockFace.Index)
                            {
                                case 0: vec = new XYZ(x, y, 0); break; // N
                                case 1: vec = new XYZ(15, y, z); break; // E
                                case 2: vec = new XYZ(x, y, 15); break; // S
                                case 3: vec = new XYZ(0, y, z); break; // W
                                case 4: vec = new XYZ(x, 15, z); break; // U
                                case 5: vec = new XYZ(x, 0, z); break; // D
                                default: vec = new XYZ(0, 0, 0); break;
                            }

                            req.Add(vec);
                        }
                    }
                }

                CuboidWithMaterial cwm = tmpCuboids[0];

                for (int i = 0; i < VoxelCuboids.Count; i++)
                {
                    FromUint(VoxelCuboids[i], cwm);

                    for (int x = cwm.X1; x < cwm.X2; x++)
                    {
                        for (int y = cwm.Y1; y < cwm.Y2; y++)
                        {
                            for (int z = cwm.Z1; z < cwm.Z2; z++)
                            {
                                // Early exit
                                if (x != 0 && x != 15 && y != 0 && y != 15 && z != 0 && z != 15) continue;

                                req.Remove(new XYZ(x, y, z));
                            }
                        }
                    }
                }

                return req.Count == 0;
            }
        }



        public void WasPlaced(Block block, string blockName)
        {
            bool collBoxCuboid = block.Attributes?.IsTrue("chiselShapeFromCollisionBox") == true;

            MaterialIds = new int[] { block.BlockId };

            if (!collBoxCuboid)
            {
                VoxelCuboids.Add(ToUint(0, 0, 0, 16, 16, 16, 0));
            } else
            {
                Cuboidf[] collboxes = block.GetCollisionBoxes(Api.World.BlockAccessor, Pos);

                for (int i = 0; i < collboxes.Length; i++)
                {
                    Cuboidf box = collboxes[i];
                    VoxelCuboids.Add(ToUint((int)(16 * box.X1), (int)(16 * box.Y1), (int)(16 * box.Z1), (int)(16 * box.X2), (int)(16 * box.Y2), (int)(16 * box.Z2), 0));
                }
            }

            this.BlockName = blockName;

            updateSideSolidSideAo();
            RegenSelectionBoxes(null);
            if (Api.Side == EnumAppSide.Client && Mesh == null)
            {
                RegenMesh();
            }
        }




        public void SetNowMaterial(byte index)
        {
            nowmaterialIndex = (byte)GameMath.Clamp(index, 0, MaterialIds.Length - 1);
        }

        public void SetNowMaterialId(int materialId)
        {
            nowmaterialIndex = (byte)Math.Max(0, MaterialIds.IndexOf(materialId));
        }



        public virtual Cuboidf[] GetSelectionBoxes(IBlockAccessor world, BlockPos pos, IPlayer forPlayer = null)
        {
            if (selectionBoxes.Length == 0) return new Cuboidf[] { Cuboidf.Default() };
            return selectionBoxes;
        }

        public Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return selectionBoxes;
        }




        #region Voxel math


        protected void convertToVoxels(out bool[,,] voxels, out byte[,,] materials)
        {
            voxels = new bool[16, 16, 16];
            materials = new byte[16, 16, 16];
            CuboidWithMaterial cwm = tmpCuboids[0];


            for (int i = 0; i < VoxelCuboids.Count; i++)
            {
                FromUint(VoxelCuboids[i], cwm);

                for (int dx = cwm.X1; dx < cwm.X2; dx++)
                {
                    for (int dy = cwm.Y1; dy < cwm.Y2; dy++)
                    {
                        for (int dz = cwm.Z1; dz < cwm.Z2; dz++)
                        {
                            voxels[dx, dy, dz] = true;
                            materials[dx, dy, dz] = cwm.Material;
                        }
                    }
                }
            }
        }

        protected void updateSideSolidSideAo()
        {
            bool[,,] Voxels;
            byte[,,] VoxelMaterial;

            convertToVoxels(out Voxels, out VoxelMaterial);
            RebuildCuboidList(Voxels, VoxelMaterial);
        }


        protected void FlipVoxels(BlockFacing frontFacing)
        {
            bool[,,] Voxels;
            byte[,,] VoxelMaterial;

            convertToVoxels(out Voxels, out VoxelMaterial);

            bool[,,] outVoxels = new bool[16, 16, 16];
            byte[,,] outVoxelMaterial = new byte[16, 16, 16];

            // Ok, now we can actually modify the voxel
            for (int dx = 0; dx < 16; dx++)
            {
                for (int dy = 0; dy < 16; dy++)
                {
                    for (int dz = 0; dz < 16; dz++)
                    {
                        outVoxels[dx, dy, dz] = Voxels[frontFacing.Axis == EnumAxis.Z ? 15 - dx : dx, dy, frontFacing.Axis == EnumAxis.X ? 15 - dz : dz];
                        outVoxelMaterial[dx, dy, dz] = VoxelMaterial[frontFacing.Axis == EnumAxis.Z ? 15 - dx : dx, dy, frontFacing.Axis == EnumAxis.X ? 15 - dz : dz];
                    }
                }
            }

            RebuildCuboidList(outVoxels, outVoxelMaterial);
        }

        protected void TransformList(int degrees, EnumAxis? flipAroundAxis, List<uint> list)
        {
            CuboidWithMaterial cwm = tmpCuboids[0];
            Vec3d axis = new Vec3d(8, 8, 8);

            for (int i = 0; i < list.Count; i++)
            {
                uint val = list[i];
                FromUint(val, cwm);

                if (flipAroundAxis == EnumAxis.X)
                {
                    cwm.X1 = 16 - cwm.X1;
                    cwm.X2 = 16 - cwm.X2;
                }
                if (flipAroundAxis == EnumAxis.Y)
                {
                    cwm.Y1 = 16 - cwm.Y1;
                    cwm.Y2 = 16 - cwm.Y2;
                }
                if (flipAroundAxis == EnumAxis.Z)
                {
                    cwm.Z1 = 16 - cwm.Z1;
                    cwm.Z2 = 16 - cwm.Z2;
                }

                Cuboidi rotated = cwm.RotatedCopy(0, -degrees, 0, axis); // Not sure why its negative

                cwm.Set(rotated.X1, rotated.Y1, rotated.Z1, rotated.X2, rotated.Y2, rotated.Z2);
                list[i] = ToUint(cwm);
            }
        }

        protected void RotateModel(int degrees, EnumAxis? flipAroundAxis)
        {
            TransformList(degrees, flipAroundAxis, VoxelCuboids);

            // Snow falls off if you flip around the block
            if (flipAroundAxis != null)
            {
                SnowCuboids = new List<uint>();
                GroundSnowCuboids = new List<uint>();
                SnowLevel = 0;
                if (Api != null) Api.World.BlockAccessor.ExchangeBlock((Block as BlockMicroBlock).notSnowCovered.Id, Pos);

            } else
            {
                TransformList(degrees, flipAroundAxis, SnowCuboids);
                TransformList(degrees, flipAroundAxis, GroundSnowCuboids);

                int shift = -degrees / 90;
                bool[] prevSolid = (bool[])sidecenterSolid.Clone();
                bool[] prevAlmostSolid = (bool[])sideAlmostSolid.Clone();

                for (int i = 0; i < 6; i++)
                {
                    sidecenterSolid[i] = prevSolid[GameMath.Mod(i + shift, 6)];
                    sideAlmostSolid[i] = prevAlmostSolid[GameMath.Mod(i + shift, 6)];
                }
            }
        }


        public void OnTransformed(ITreeAttribute tree, int byDegrees, EnumAxis? flipAroundAxis)
        {
            uint[] cuboidValues = (tree["cuboids"] as IntArrayAttribute)?.AsUint;
            VoxelCuboids = cuboidValues == null ? new List<uint>(0) : new List<uint>(cuboidValues);
            uint[] snowcuboidValues = (tree["snowcuboids"] as IntArrayAttribute)?.AsUint;
            SnowCuboids = snowcuboidValues == null ? new List<uint>(0) : new List<uint>(snowcuboidValues);
            uint[] groundsnowvalues = (tree["groundSnowCuboids"] as IntArrayAttribute)?.AsUint;
            GroundSnowCuboids = groundsnowvalues == null ? new List<uint>(0) : new List<uint>(groundsnowvalues);


            RotateModel(byDegrees, flipAroundAxis);


            tree["cuboids"] = new IntArrayAttribute(VoxelCuboids.ToArray());
            tree["snowcuboids"] = new IntArrayAttribute(SnowCuboids.ToArray());
            tree["groundSnowCuboids"] = new IntArrayAttribute(GroundSnowCuboids.ToArray());
        }



        public bool SetVoxel(Vec3i voxelPos, bool state, IPlayer byPlayer, byte materialId, int size)
        {
            bool[,,] Voxels;
            byte[,,] VoxelMaterial;

            convertToVoxels(out Voxels, out VoxelMaterial);

            // Ok, now we can actually modify the voxel
            bool wasChanged = false;
            for (int dx = 0; dx < size; dx++)
            {
                for (int dy = 0; dy < size; dy++)
                {
                    for (int dz = 0; dz < size; dz++)
                    {
                        if (voxelPos.X + dx >= 16 || voxelPos.Y + dy >= 16 || voxelPos.Z + dz >= 16) continue;

                        wasChanged |= Voxels[voxelPos.X + dx, voxelPos.Y + dy, voxelPos.Z + dz] != state;

                        Voxels[voxelPos.X + dx, voxelPos.Y + dy, voxelPos.Z + dz] = state;

                        if (state)
                        {
                            VoxelMaterial[voxelPos.X + dx, voxelPos.Y + dy, voxelPos.Z + dz] = materialId;
                        }
                    }
                }
            }

            if (!wasChanged) return false;

            RebuildCuboidList(Voxels, VoxelMaterial);

            return true;
        }



        public void SetData(bool[,,] Voxels, byte[,,] VoxelMaterial)
        {
            RebuildCuboidList(Voxels, VoxelMaterial);

            if (Api.Side == EnumAppSide.Client)
            {
                RegenMesh();
            }

            RegenSelectionBoxes(null);
            MarkDirty(true);

            if (VoxelCuboids.Count == 0)
            {
                Api.World.BlockAccessor.SetBlock(0, Pos);
                return;
            }
        }


        #region Side AO 


        public bool DoEmitSideAo(int facing)
        {
            return (emitSideAo & (1 << facing)) != 0;
        }

        public bool DoEmitSideAoByFlag(int flag)
        {
            return (emitSideAo & flag) != 0;
        }

        #endregion


        protected void RebuildCuboidList(bool[,,] Voxels, byte[,,] VoxelMaterial)
        {
            bool[,,] VoxelVisited = new bool[16, 16, 16];
            emitSideAo = 0x3F;
            sidecenterSolid = new bool[] { true, true, true, true, true, true };
            float voxelCount = 0;

            // And now let's rebuild the cuboids with some greedy search algo thing
            VoxelCuboids.Clear();

            int[] edgeVoxelsMissing = new int[6];
            int[] edgeCenterVoxelsMissing = new int[6];

            byte[] lightshv = this.LightHsv;

            for (int dx = 0; dx < 16; dx++)
            {
                for (int dy = 0; dy < 16; dy++)
                {
                    for (int dz = 0; dz < 16; dz++)
                    {
                        bool isVoxel = Voxels[dx, dy, dz];

                        // North: Negative Z
                        // East: Positive X
                        // South: Positive Z
                        // West: Negative X
                        // Up: Positive Y
                        // Down: Negative Y
                        if (!isVoxel)
                        {
                            if (dz == 0)
                            {
                                edgeVoxelsMissing[BlockFacing.NORTH.Index]++;
                                if (Math.Abs(dy - 8) < 5 && Math.Abs(dx - 8) < 5) edgeCenterVoxelsMissing[BlockFacing.NORTH.Index]++;
                            }
                            if (dx == 15)
                            {
                                edgeVoxelsMissing[BlockFacing.EAST.Index]++;
                                if (Math.Abs(dy - 8) < 5 && Math.Abs(dz - 8) < 5) edgeCenterVoxelsMissing[BlockFacing.EAST.Index]++;
                            }
                            if (dz == 15)
                            {
                                edgeVoxelsMissing[BlockFacing.SOUTH.Index]++;
                                if (Math.Abs(dy - 8) < 5 && Math.Abs(dx - 8) < 5) edgeCenterVoxelsMissing[BlockFacing.SOUTH.Index]++;
                            }
                            if (dx == 0)
                            {
                                edgeVoxelsMissing[BlockFacing.WEST.Index]++;
                                if (Math.Abs(dy - 8) < 5 && Math.Abs(dz - 8) < 5) edgeCenterVoxelsMissing[BlockFacing.WEST.Index]++;
                            }
                            if (dy == 15)
                            {
                                edgeVoxelsMissing[BlockFacing.UP.Index]++;
                                if (Math.Abs(dz - 8) < 5 && Math.Abs(dx - 8) < 5) edgeCenterVoxelsMissing[BlockFacing.UP.Index]++;
                            }
                            if (dy == 0)
                            {
                                edgeVoxelsMissing[BlockFacing.DOWN.Index]++;
                                if (Math.Abs(dz - 8) < 5 && Math.Abs(dx - 8) < 5) edgeCenterVoxelsMissing[BlockFacing.DOWN.Index]++;
                            }
                            continue;
                        } else
                        {
                            voxelCount++;
                        }

                        if (VoxelVisited[dx, dy, dz]) continue;

                        CuboidWithMaterial cub = new CuboidWithMaterial()
                        {
                            Material = VoxelMaterial[dx, dy, dz],
                            X1 = dx, Y1 = dy, Z1 = dz,
                            X2 = dx + 1, Y2 = dy + 1, Z2 = dz + 1
                        };

                        // Try grow this cuboid for as long as we can
                        bool didGrowAny = true;
                        while (didGrowAny)
                        {
                            didGrowAny = false;
                            didGrowAny |= TryGrowX(cub, Voxels, VoxelVisited, VoxelMaterial);
                            didGrowAny |= TryGrowY(cub, Voxels, VoxelVisited, VoxelMaterial);
                            didGrowAny |= TryGrowZ(cub, Voxels, VoxelVisited, VoxelMaterial);
                        }

                        VoxelCuboids.Add(ToUint(cub));
                    }
                }
            }

            bool doEmitSideAo = edgeVoxelsMissing[0] < 64 || edgeVoxelsMissing[1] < 64 || edgeVoxelsMissing[2] < 64 || edgeVoxelsMissing[3] < 64;

            if (absorbAnyLight != doEmitSideAo)
            {
                int preva = GetLightAbsorption();
                absorbAnyLight = doEmitSideAo;
                int nowa = GetLightAbsorption();
                if (preva != nowa)
                {
                    Api.World.BlockAccessor.MarkAbsorptionChanged(preva, nowa, Pos);
                }
            }

            for (int i = 0; i < 6; i++)
            {
                sidecenterSolid[i] = edgeCenterVoxelsMissing[i] < 5;
                sideAlmostSolid[i] = edgeVoxelsMissing[i] <= 32;
            }
            emitSideAo = lightshv[2] < 10 && doEmitSideAo ? 0x3F : 0;

            this.sizeRel = voxelCount / (16f * 16f * 16f);

            buildSnowCuboids(Voxels);
        }

        void buildSnowCuboids(bool[,,] Voxels) 
        {
            SnowCuboids.Clear();
            GroundSnowCuboids.Clear();

            //if (SnowLevel > 0) - always generate this
            {
                bool[,] snowVoxelVisited = new bool[16, 16];

                for (int dx = 0; dx < 16; dx++)
                {
                    for (int dz = 0; dz < 16; dz++)
                    {
                        if (snowVoxelVisited[dx, dz]) continue;

                        for (int dy = 15; dy >= 0; dy--)
                        {
                            bool ground = dy == 0;
                            bool solid = ground || Voxels[dx, dy, dz];

                            if (solid)
                            {
                                CuboidWithMaterial cub = new CuboidWithMaterial()
                                {
                                    Material = 0,
                                    X1 = dx,
                                    Y1 = dy,
                                    Z1 = dz,
                                    X2 = dx + 1,
                                    Y2 = dy + 1,
                                    Z2 = dz + 1
                                };

                                // Try grow this cuboid for as long as we can
                                bool didGrowAny = true;
                                while (didGrowAny)
                                {
                                    didGrowAny = false;
                                    didGrowAny |= TrySnowGrowX(cub, Voxels, snowVoxelVisited);
                                    didGrowAny |= TrySnowGrowZ(cub, Voxels, snowVoxelVisited);
                                }

                                if (ground)
                                {
                                    GroundSnowCuboids.Add(ToUint(cub));
                                }
                                else
                                {
                                    SnowCuboids.Add(ToUint(cub));
                                }

                                break;
                            }
                        }
                    }
                }
            }
        }


        protected bool TryGrowX(CuboidWithMaterial cub, bool[,,] voxels, bool[,,] voxelVisited, byte[,,] voxelMaterial)
        {
            if (cub.X2 > 15) return false;

            for (int y = cub.Y1; y < cub.Y2; y++)
            {
                for (int z = cub.Z1; z < cub.Z2; z++)
                {
                    if (!voxels[cub.X2, y, z] || voxelVisited[cub.X2, y, z] || voxelMaterial[cub.X2, y, z] != cub.Material) return false;
                }
            }

            for (int y = cub.Y1; y < cub.Y2; y++)
            {
                for (int z = cub.Z1; z < cub.Z2; z++)
                {
                    voxelVisited[cub.X2, y, z] = true;
                }
            }

            cub.X2++;
            return true;
        }

        protected bool TryGrowY(CuboidWithMaterial cub, bool[,,] voxels, bool[,,] voxelVisited, byte[,,] voxelMaterial)
        {
            if (cub.Y2 > 15) return false;

            for (int x = cub.X1; x < cub.X2; x++)
            {
                for (int z = cub.Z1; z < cub.Z2; z++)
                {
                    if (!voxels[x, cub.Y2, z] || voxelVisited[x, cub.Y2, z] || voxelMaterial[x, cub.Y2, z] != cub.Material) return false;
                }
            }

            for (int x = cub.X1; x < cub.X2; x++)
            {
                for (int z = cub.Z1; z < cub.Z2; z++)
                {
                    voxelVisited[x, cub.Y2, z] = true;
                }
            }

            cub.Y2++;
            return true;
        }

        protected bool TryGrowZ(CuboidWithMaterial cub, bool[,,] voxels, bool[,,] voxelVisited, byte[,,] voxelMaterial)
        {
            if (cub.Z2 > 15) return false;

            for (int x = cub.X1; x < cub.X2; x++)
            {
                for (int y = cub.Y1; y < cub.Y2; y++)
                {
                    if (!voxels[x, y, cub.Z2] || voxelVisited[x, y, cub.Z2] || voxelMaterial[x, y, cub.Z2] != cub.Material) return false;
                }
            }

            for (int x = cub.X1; x < cub.X2; x++)
            {
                for (int y = cub.Y1; y < cub.Y2; y++)
                {
                    voxelVisited[x, y, cub.Z2] = true;
                }
            }

            cub.Z2++;
            return true;
        }




        #region Snowgrow

        protected bool TrySnowGrowX(CuboidWithMaterial cub, bool[,,] voxels, bool[,] voxelVisited)
        {
            if (cub.X2 > 15) return false;

            for (int z = cub.Z1; z < cub.Z2; z++)
            {
                if (!voxels[cub.X2, cub.Y1, z] || voxelVisited[cub.X2, z] || (cub.Y2 < 15 && voxels[cub.X2, cub.Y2, z])) return false;
            }

            for (int z = cub.Z1; z < cub.Z2; z++)
            {
                voxelVisited[cub.X2, z] = true;
            }

            cub.X2++;
            return true;
        }

        protected bool TrySnowGrowZ(CuboidWithMaterial cub, bool[,,] voxels, bool[,] voxelVisited)
        {
            if (cub.Z2 > 15) return false;

            for (int x = cub.X1; x < cub.X2; x++)
            {
                // Stop if
                // "Floor" is gone, already visited, or there's a voxel above
                if (!voxels[x, cub.Y1, cub.Z2] || voxelVisited[x, cub.Z2] || (cub.Y2 < 15 && voxels[x, cub.Y2, cub.Z2])) return false;
            }

            for (int x = cub.X1; x < cub.X2; x++)
            {
                voxelVisited[x, cub.Z2] = true;
            }

            cub.Z2++;
            return true;
        }



        #endregion



        public virtual void RegenSelectionBoxes(IPlayer byPlayer)
        {
            // Create a temporary array first, because the offthread particle system might otherwise access a null collisionbox
            Cuboidf[] selectionBoxesTmp = new Cuboidf[VoxelCuboids.Count];
            CuboidWithMaterial cwm = tmpCuboids[0];

            totalVoxels = 0;

            for (int i = 0; i < VoxelCuboids.Count; i++)
            {
                FromUint(VoxelCuboids[i], cwm);
                selectionBoxesTmp[i] = cwm.ToCuboidf();

                totalVoxels += cwm.Volume;
            }
            this.selectionBoxes = selectionBoxesTmp;
        }



        #endregion

        #region Mesh generation
        public void RegenMesh()
        {
            Mesh = CreateMesh(Api as ICoreClientAPI, VoxelCuboids, MaterialIds, Pos);
            GenSnowMesh();
        }

        private void GenSnowMesh()
        {
            if (SnowCuboids.Count > 0 && SnowLevel > 0)
            {
                SnowMesh = CreateMesh(Api as ICoreClientAPI, SnowCuboids, new int[] { snowLayerBlockId }, Pos);
                SnowMesh.Translate(0, 1 / 16f, 0);

                if (Api.World.BlockAccessor.GetBlock(Pos.DownCopy()).SideSolid[BlockFacing.UP.Index])
                {
                    SnowMesh.AddMeshData(CreateMesh(Api as ICoreClientAPI, GroundSnowCuboids, new int[] { snowLayerBlockId }, Pos));
                }
            }
            else
            {
                SnowMesh = null;
            }
        }

        public void RegenMesh(ICoreClientAPI capi)
        {
            Mesh = CreateMesh(capi, VoxelCuboids, MaterialIds, Pos);
        }

        static int[] drawFaceIndexLookup = new int[]
        {
            BlockFacing.WEST.Index,
            BlockFacing.DOWN.Index,
            BlockFacing.NORTH.Index,
            BlockFacing.EAST.Index,
            BlockFacing.UP.Index,
            BlockFacing.SOUTH.Index
        };

        public static MeshData CreateMesh(ICoreClientAPI coreClientAPI, List<uint> voxelCuboids, int[] materials, BlockPos posForRnd = null)
        {
            MeshData mesh = new MeshData(24, 36, false).WithColorMaps().WithRenderpasses().WithXyzFaces();
            if (voxelCuboids == null || materials == null) return mesh;
            CuboidWithMaterial[] cwms = tmpCuboids;

            bool[] skipFace = new bool[6];

            for (int i = 0; i < voxelCuboids.Count; i++)
            {
                FromUint(voxelCuboids[i], cwms[i]);
            }

            var blocks = coreClientAPI.World.Blocks;
            bool[] matsTransparent = new bool[materials.Length];
            for (int i = 0; i < materials.Length; i++) matsTransparent[i] = blocks[materials[i]].RenderPass == EnumChunkRenderPass.Transparent;

            for (int i = 0; i < voxelCuboids.Count; i++) 
            {
                CuboidWithMaterial cwm = cwms[i];
                skipFace[0] = skipFace[1] = skipFace[2] = skipFace[3] = skipFace[4] = skipFace[5] = false;

                for (int j = 0; j < voxelCuboids.Count; j++)
                {
                    CuboidWithMaterial cwmNeib = cwms[j];

                    if (cwmNeib.Material >= matsTransparent.Length)
                    {
                        coreClientAPI.Logger.Error("Microblock at {0} corrupted. A cuboid has material index {1}, but there's only {2} materials. Block will be invisible.", posForRnd, cwmNeib.Material, matsTransparent.Length);
                        return mesh;
                    }

                    if (i == j || matsTransparent[cwmNeib.Material]) continue;

                    for (int axis = 0; axis < 3; axis++)
                    {
                        skipFace[drawFaceIndexLookup[axis]] |= cwm[axis] == cwmNeib[axis + 3] && cwmNeib.ContainsOrTouches(cwm, axis);
                        skipFace[drawFaceIndexLookup[axis + 3]] |= cwm[axis + 3] == cwmNeib[axis] && cwmNeib.ContainsOrTouches(cwm, axis);
                    }
                }


                Block block = coreClientAPI.World.GetBlock(materials[cwm.Material]);

                float subPixelPaddingx = coreClientAPI.BlockTextureAtlas.SubPixelPaddingX;
                float subPixelPaddingy = coreClientAPI.BlockTextureAtlas.SubPixelPaddingY;

                int altNum = 0;

                if (block.HasAlternates && posForRnd != null)
                {
                    int altcount = 0;
                    foreach (var val in block.Textures)
                    {
                        BakedCompositeTexture bct = val.Value.Baked;
                        if (bct.BakedVariants == null) continue;
                        altcount = Math.Max(altcount, bct.BakedVariants.Length);
                    }

                    if (altcount > 0)  // block.HasAlternates might indicate alternate shapes, but no alternate textures!
                    {
                        altNum = block.RandomizeAxes == EnumRandomizeAxes.XYZ ? GameMath.MurmurHash3Mod(posForRnd.X, posForRnd.Y, posForRnd.Z, altcount) : GameMath.MurmurHash3Mod(posForRnd.X, 0, posForRnd.Z, altcount);
                    }
                }

                MeshData cuboidmesh = genCube(
                    cwm.X1, cwm.Y1, cwm.Z1,
                    cwm.X2 - cwm.X1, cwm.Y2 - cwm.Y1, cwm.Z2 - cwm.Z1, 
                    coreClientAPI, 
                    coreClientAPI.Tesselator.GetTexSource(block, altNum, true),
                    subPixelPaddingx,
                    subPixelPaddingy,
                    block
                );

                AddMeshData(mesh, cuboidmesh, skipFace);
            }

            return mesh;
        }



        protected static void AddMeshData(MeshData targetMesh, MeshData sourceMesh, bool[] skipFaces)
        {
            int index = 0;
            int start = targetMesh.IndicesCount > 0 ? (targetMesh.Indices[targetMesh.IndicesCount - 1] + 1) : 0;

            for (int face = 0; face < 6; face++)
            {
                if (skipFaces[face]) continue;

                for (int i = 0; i < 4; i++)
                {
                    int ind = face * 4 + i;

                    if (targetMesh.VerticesCount >= targetMesh.VerticesMax)
                    {
                        targetMesh.GrowVertexBuffer();
                        targetMesh.GrowNormalsBuffer();
                    }

                    targetMesh.xyz[targetMesh.XyzCount + 0] = sourceMesh.xyz[ind * 3 + 0];
                    targetMesh.xyz[targetMesh.XyzCount + 1] = sourceMesh.xyz[ind * 3 + 1];
                    targetMesh.xyz[targetMesh.XyzCount + 2] = sourceMesh.xyz[ind * 3 + 2];

                    targetMesh.Uv[targetMesh.UvCount + 0] = sourceMesh.Uv[ind * 2 + 0];
                    targetMesh.Uv[targetMesh.UvCount + 1] = sourceMesh.Uv[ind * 2 + 1];


                    targetMesh.Rgba[targetMesh.RgbaCount + 0] = sourceMesh.Rgba[ind * 4 + 0];
                    targetMesh.Rgba[targetMesh.RgbaCount + 1] = sourceMesh.Rgba[ind * 4 + 1];
                    targetMesh.Rgba[targetMesh.RgbaCount + 2] = sourceMesh.Rgba[ind * 4 + 2];
                    targetMesh.Rgba[targetMesh.RgbaCount + 3] = sourceMesh.Rgba[ind * 4 + 3];

                    targetMesh.Flags[targetMesh.VerticesCount] = sourceMesh.Flags[ind];
                    targetMesh.VerticesCount++;
                }

                for (int i = 0; i < 6; i++)
                {
                    targetMesh.AddIndex(start + sourceMesh.Indices[index++]);
                }

                targetMesh.AddXyzFace(sourceMesh.XyzFaces[face]);
                targetMesh.AddColorMapIndex(sourceMesh.ClimateColorMapIds[face], sourceMesh.SeasonColorMapIds[face]);
                targetMesh.AddRenderPass(sourceMesh.RenderPassesAndExtraBits[face]);
            }
        }




        public MeshData CreateDecalMesh(ITexPositionSource decalTexSource)
        {
            return CreateDecalMesh(Api as ICoreClientAPI, VoxelCuboids, decalTexSource);
        }

        public static MeshData CreateDecalMesh(ICoreClientAPI coreClientAPI, List<uint> voxelCuboids, ITexPositionSource decalTexSource)
        {
            MeshData mesh = new MeshData(24, 36, false).WithColorMaps().WithRenderpasses().WithXyzFaces();

            CuboidWithMaterial[] cwms = tmpCuboids;
            bool[] skipFace = new bool[6];
            for (int i = 0; i < voxelCuboids.Count; i++)
            {
                FromUint(voxelCuboids[i], cwms[i]);
            }


            for (int i = 0; i < voxelCuboids.Count; i++)
            {
                CuboidWithMaterial cwm = cwms[i];
                skipFace[0] = skipFace[1] = skipFace[2] = skipFace[3] = skipFace[4] = skipFace[5] = false;

                for (int j = 0; j < voxelCuboids.Count; j++)
                {
                    if (i == j) continue;
                    CuboidWithMaterial cwmNeib = cwms[j];

                    for (int axis = 0; axis < 3; axis++)
                    {
                        skipFace[drawFaceIndexLookup[axis]] |= cwm[axis] == cwmNeib[axis + 3] && cwmNeib.ContainsOrTouches(cwm, axis);
                        skipFace[drawFaceIndexLookup[axis + 3]] |= cwm[axis + 3] == cwmNeib[axis] && cwmNeib.ContainsOrTouches(cwm, axis);
                    }
                }

                MeshData cuboidmesh = genCube(
                    cwm.X1, cwm.Y1, cwm.Z1,
                    cwm.X2 - cwm.X1, cwm.Y2 - cwm.Y1, cwm.Z2 - cwm.Z1,
                    coreClientAPI,
                    decalTexSource,
                    0,
                    0,
                    coreClientAPI.World.GetBlock(0)
                );

                AddMeshData(mesh, cuboidmesh, skipFace);
            }

            return mesh;
        }


        [ThreadStatic]
        static MeshData bCm = null;


        // Re-uses all arrays and just resets data where needed
        static MeshData GetCubeMeshFast(float scaleX, float scaleY, float scaleZ, Vec3f translate)
        {
            if (bCm == null)
            {
                bCm = CubeMeshUtil.GetCube();
                bCm.Rgba.Fill((byte)255);
                bCm.Flags = new int[bCm.VerticesCount];
                bCm.RenderPassesAndExtraBits = new short[bCm.VerticesCount / 4];
                bCm.RenderPassCount = bCm.VerticesCount / 4;
                bCm.ColorMapIdsCount = bCm.VerticesCount / 4;
                bCm.ClimateColorMapIds = new byte[bCm.VerticesCount / 4];
                bCm.SeasonColorMapIds = new byte[bCm.VerticesCount / 4];
                bCm.XyzFaces = new byte[bCm.VerticesCount / 4];
                bCm.XyzFacesCount = bCm.VerticesCount / 4;
            }
            else
            {
                // Reset xyz and UV to default size
                for (int i = 0; i < 3 * 4 * 6; i++)
                {
                    bCm.xyz[i] = CubeMeshUtil.CubeVertices[i];
                }
                for (int i = 0; i < 2 * 4 * 6; i++)
                {
                    bCm.Uv[i] = CubeMeshUtil.CubeUvCoords[i];
                }
            }

            CubeMeshUtil.ScaleCubeMesh(bCm, scaleX, scaleY, scaleZ, translate);

            return bCm;
        }




        protected static MeshData genCube(int voxelX, int voxelY, int voxelZ, int width, int height, int length, ICoreClientAPI capi, ITexPositionSource texSource, float subPixelPaddingx, float subPixelPaddingy, Block block)
        {
            short renderpass = (short)block.RenderPass;
            int renderFlags = block.VertexFlags.All;

            MeshData mesh = GetCubeMeshFast(width / 32f, height / 32f, length / 32f, new Vec3f(voxelX / 16f, voxelY / 16f, voxelZ / 16f));

            bCm.Flags.Fill(renderFlags);
            for (int i = 0; i < bCm.RenderPassCount; i++)
            {
                bCm.RenderPassesAndExtraBits[i] = renderpass;
            }

            int k = 0;
            for (int i = 0; i < 6; i++)
            {
                BlockFacing facing = BlockFacing.ALLFACES[i];

                mesh.XyzFaces[i] = facing.MeshDataIndex;

                int normal = facing.NormalPackedFlags;
                mesh.Flags[i * 4 + 0] |= normal;
                mesh.Flags[i * 4 + 1] |= normal;
                mesh.Flags[i * 4 + 2] |= normal;
                mesh.Flags[i * 4 + 3] |= normal;

                bool isOutside =
                    (
                        (facing == BlockFacing.NORTH && voxelZ == 0) ||
                        (facing == BlockFacing.EAST && voxelX + width == 16) ||
                        (facing == BlockFacing.SOUTH && voxelZ + length == 16) ||
                        (facing == BlockFacing.WEST && voxelX == 0) ||
                        (facing == BlockFacing.UP && voxelY + height == 16) ||
                        (facing == BlockFacing.DOWN && voxelY == 0)
                    )
                ;
                 

                TextureAtlasPosition tpos = isOutside ? texSource[facing.Code] : texSource["inside-" + facing.Code];
                if (tpos == null)
                {
                    tpos = texSource[facing.Code];
                }
                if (tpos == null && block.Textures.Count > 0)
                {
                    tpos = texSource[block.Textures.First().Key];
                }
                if (tpos == null)
                {
                    tpos = capi.BlockTextureAtlas.UnknownTexturePosition;
                }

                float texWidth = tpos.x2 - tpos.x1;
                float texHeight = tpos.y2 - tpos.y1;

                for (int j = 0; j < 2*4; j++)
                {
                    if (j % 2 > 0)
                    {
                        mesh.Uv[k] = tpos.y1 + mesh.Uv[k] * texHeight - subPixelPaddingy;
                    } else
                    {
                        mesh.Uv[k] = tpos.x1 + mesh.Uv[k] * texWidth - subPixelPaddingx;
                    }
                    
                    k++;
                }

            }
            
            return mesh;
        }




        #endregion


        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            MaterialIds = MaterialIdsFromAttributes(tree, worldAccessForResolve);
            BlockName = tree.GetString("blockName", "");

            uint[] values = (tree["cuboids"] as IntArrayAttribute)?.AsUint;
            // When loaded from json
            if (values == null)
            {
                values = (tree["cuboids"] as LongArrayAttribute)?.AsUint;
            }
            if (values == null)
            {
                values = new uint[] { ToUint(0,0,0, 16, 16, 16, 0) };
            }
            VoxelCuboids = new List<uint>(values);

            uint[] snowvalues = (tree["snowcuboids"] as IntArrayAttribute)?.AsUint;
            uint[] groundsnowvalues = (tree["groundSnowCuboids"] as IntArrayAttribute)?.AsUint;
            if (snowvalues != null && groundsnowvalues != null)
            {
                SnowCuboids = new List<uint>(snowvalues);
                GroundSnowCuboids = new List<uint>(groundsnowvalues);
            } else
            {
                bool[,,] Voxels;
                byte[,,] VoxelMaterial;
                convertToVoxels(out Voxels, out VoxelMaterial);
                buildSnowCuboids(Voxels);
            }

            byte[] sideAo = tree.GetBytes("emitSideAo", new byte[] { 255 });
            if (sideAo.Length > 0)
            {
                emitSideAo = sideAo[0];

                absorbAnyLight = emitSideAo != 0;
            }

            byte[] sideSolid = tree.GetBytes("sideSolid", new byte[] { 255 });
            if (sideSolid.Length > 0)
            {
                GameMath.BoolsFromInt(this.sidecenterSolid, sideSolid[0]);
            }

            byte[] sideAlmostSolid = tree.GetBytes("sideAlmostSolid", new byte[] { 255 });
            if (sideAlmostSolid.Length > 0)
            {
                GameMath.BoolsFromInt(this.sideAlmostSolid, sideAlmostSolid[0]);
            }


            if (worldAccessForResolve.Side == EnumAppSide.Client)
            {
                RegenMesh(worldAccessForResolve.Api as ICoreClientAPI);
            } else
            {
                // From 1.15.0 until 1.15.5 we forgot to store sideAlmostSolid
                if (!tree.HasAttribute("sideAlmostSolid"))
                {
                    if (Api == null) this.Api = worldAccessForResolve.Api; // Needed for LightHsv property, I hope this does not break things >.>
                    updateSideSolidSideAo();
                }
            }

            RegenSelectionBoxes(null);
            if (noMesh)
            {
                MarkDirty(true);
                noMesh = false;
            }
        }


        public static int[] MaterialIdsFromAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            if (tree["materials"] is IntArrayAttribute)
            {
                // Pre 1.8 storage and Post 1.13-pre.2 storage
                int[] ids = (tree["materials"] as IntArrayAttribute).value;

                int[] valuesInt = new int[ids.Length];
                for (int i = 0; i < ids.Length; i++)
                {
                    valuesInt[i] = ids[i];
                }

                return valuesInt;
            }
            else
            {
                if (!(tree["materials"] is StringArrayAttribute))
                {
                    return new int[] { worldAccessForResolve.GetBlock(new AssetLocation("rock-granite")).Id };
                }

                string[] codes = (tree["materials"] as StringArrayAttribute).value;
                int[] ids = new int[codes.Length];
                for (int i = 0; i < ids.Length; i++)
                {
                    Block block = worldAccessForResolve.GetBlock(new AssetLocation(codes[i]));
                    if (block == null)
                    {
                        block = worldAccessForResolve.GetBlock(new AssetLocation(codes[i] + "-free")); // pre 1.13 blocks

                        if (block == null)
                        {
                            block = worldAccessForResolve.GetBlock(new AssetLocation("rock-granite"));
                        }
                    }

                    ids[i] = block.BlockId;
                }

                return ids;
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            IntArrayAttribute attr = new IntArrayAttribute();
            attr.value = MaterialIds;

            if (attr.value != null)
            {
                tree["materials"] = attr;
            }

            
            tree["cuboids"] = new IntArrayAttribute(VoxelCuboids.ToArray());

            if (SnowCuboids.Count > 0)
            {
                tree["snowcuboids"] = new IntArrayAttribute(SnowCuboids.ToArray());
            }
            if (GroundSnowCuboids.Count > 0)
            {
                tree["groundSnowCuboids"] = new IntArrayAttribute(GroundSnowCuboids.ToArray());
            }

            tree.SetBytes("emitSideAo", new byte[] { (byte) emitSideAo });

            tree.SetBytes("sideSolid", new byte[] { (byte) GameMath.IntFromBools(sidecenterSolid) });

            tree.SetBytes("sideAlmostSolid", new byte[] { (byte) GameMath.IntFromBools(sideAlmostSolid) });

            tree.SetString("blockName", BlockName);
        }

        bool noMesh = false;

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
        {
            if (Mesh == null)
            {
                noMesh = true;
                return false;
            }

            mesher.AddMeshData(Mesh);

            Block = Api.World.BlockAccessor.GetBlock(Pos);
            SnowLevel = (int)Block.snowLevel;
            if (PrevSnowLevel != SnowLevel || SnowMesh == null)
            {
                GenSnowMesh();
                PrevSnowLevel = SnowLevel;
            }

            mesher.AddMeshData(SnowMesh);

            return true;
        }


        public static uint ToUint(int minx, int miny, int minz, int maxx, int maxy, int maxz, int material)
        {
            Debug.Assert(maxx > 0 && maxx > minx);
            Debug.Assert(maxy > 0 && maxy > miny);
            Debug.Assert(maxz > 0 && maxz > minz);
            Debug.Assert(minx < 16);
            Debug.Assert(miny < 16);
            Debug.Assert(minz < 16);

            return (uint)(minx | (miny << 4) | (minz << 8) | ((maxx - 1) << 12) | ((maxy - 1) << 16) | ((maxz - 1) << 20) | (material << 24));
        }

        protected uint ToUint(CuboidWithMaterial cub)
        {
            return (uint)(cub.X1 | (cub.Y1 << 4) | (cub.Z1 << 8) | ((cub.X2 - 1) << 12) | ((cub.Y2 - 1) << 16) | ((cub.Z2 - 1) << 20) | (cub.Material << 24));
        }


        public static void FromUint(uint val, CuboidWithMaterial tocuboid)
        {
            tocuboid.X1 = (int)((val) & 15);
            tocuboid.Y1 = (int)((val >> 4) & 15);
            tocuboid.Z1 = (int)((val >> 8) & 15);
            tocuboid.X2 = (int)(((val) >> 12) & 15) + 1;
            tocuboid.Y2 = (int)(((val) >> 16) & 15) + 1;
            tocuboid.Z2 = (int)(((val) >> 20) & 15) + 1;
            tocuboid.Material = (byte)((val >> 24) & 15);
        }

        public override void OnLoadCollectibleMappings(IWorldAccessor worldForNewMappings, Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed)
        {
            base.OnLoadCollectibleMappings(worldForNewMappings, oldBlockIdMapping, oldItemIdMapping, schematicSeed);

            for (int i = 0; i < MaterialIds.Length; i++)
            {
                AssetLocation code;
                if (oldBlockIdMapping.TryGetValue(MaterialIds[i], out code))
                {
                    Block block = worldForNewMappings.GetBlock(code);
                    if (block == null)
                    {
                        worldForNewMappings.Logger.Warning("Cannot load chiseled block id mapping @ {1}, block code {0} not found block registry. Will not display correctly.", code, Pos);
                        continue;
                    }

                    MaterialIds[i] = block.Id;
                } else
                {
                    worldForNewMappings.Logger.Warning("Cannot load chiseled block id mapping @ {1}, block id {0} not found block registry. Will not display correctly.", MaterialIds[i], Pos);
                }
            }
        }

        public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
        {
            base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);

            for (int i = 0; i < MaterialIds.Length; i++)
            {
                Block block = Api.World.GetBlock(MaterialIds[i]);
                blockIdMapping[MaterialIds[i]] = block.Code;
            }
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);

            dsc.AppendLine(BlockName.Substring(BlockName.IndexOf('\n') + 1));

            if (forPlayer?.CurrentBlockSelection?.Face != null && MaterialIds != null)
            {
                Block block = Api.World.GetBlock(MaterialIds[0]);
                var mat = block.BlockMaterial;
                if (mat == EnumBlockMaterial.Ore || mat == EnumBlockMaterial.Stone || mat == EnumBlockMaterial.Soil || mat == EnumBlockMaterial.Ceramic)
                {
                    if (sideAlmostSolid[forPlayer.CurrentBlockSelection.Face.Index] && VolumeRel >= 0.5f)
                    {
                        dsc.AppendLine("Insulating block face");
                    }
                }
            }
             
        }
    }



    struct XYZ : IEquatable<XYZ>
    {
        public int X;
        public int Y;
        public int Z;

        public XYZ(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(XYZ other)
        {
            return other.X == X && other.Y == Y && other.Z == Z;
        }
    }
}
