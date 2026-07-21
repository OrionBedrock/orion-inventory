namespace OrionInventory.Handlers;

using Orion.Api;
using Orion.Protocol.Io;
using Orion.Protocol.Packets;
using OrionInventory;

public static class MobEquipmentHandler
{
    public static void Handle(IPlayer player, ReadOnlySpan<byte> packetBuffer)
    {
        MobEquipmentPacket packet = (MobEquipmentPacket)PacketCodec.DeserializeFromBytes(packetBuffer);

        if (packet.EntityRuntimeId != 0 && packet.EntityRuntimeId != player.RuntimeId)
        {
            return;
        }

        EntityInventoryTrait? inventory = player.GetTrait<EntityInventoryTrait>();
        if (inventory is null)
        {
            return;
        }

        if (packet.HotBarSlot < 9)
        {
            inventory.SetHeldItem(packet.HotBarSlot);
        }
    }
}
