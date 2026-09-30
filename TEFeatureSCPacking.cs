using System;
using UnityEngine.Scripting;

namespace SurvivorCommunity
{
    /// <summary>
    /// Marker feature on the packing station block (Config/blocks.xml scPackingStation, next to its
    /// TEFeatureStorage). It carries no data and adds no commands: opening the station is vanilla's loot
    /// window (the storage feature's "Search"), and the packing strip appended into that window recognises
    /// the station by this feature's presence on the composite tile entity.
    /// </summary>
    [Preserve]
    public class TEFeatureSCPacking : TEFeatureAbs
    {
        public TEFeatureSCPacking()
        {
        }

        public override void Init(TileEntityComposite _parent, TileEntityFeatureData _featureData)
        {
            base.Init(_parent, _featureData);
            try
            {
                string hash = (_featureData != null) ? _featureData.NameHash.ToString("X8") : "????????";
                ScLog.OnceOut("packing-registered", "packing feature registered on the packing station (hash 0x" + hash + ")");
            }
            catch (Exception e)
            {
                ScLog.Once("TEFeatureSCPacking.Init", e);
            }
        }

        public override void CopyFromInternal(TileEntityComposite _other)
        {
        }

        public override void Write(PooledBinaryWriter _bw, TileEntity.StreamModeWrite _eStreamMode)
        {
            base.Write(_bw, _eStreamMode);
        }

        public override void Read(PooledBinaryReader _br, TileEntity.StreamModeRead _eStreamMode)
        {
            base.Read(_br, _eStreamMode);
        }
    }
}
