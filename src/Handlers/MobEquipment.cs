namespace OrionInventory.Handlers;

using Orion;
using Orion.Network;
using Orion.Protocol.Packets;
using Orion.RakNet;
using OrionInventory;
using Orion.Network.Handlers;
using Orion.Protocol.Io;
using BinaryReader = Basalt.Binary.BinaryReader;

public static class MobEquipmentHandler
{
    public static void Handle(Server server, NetworkConnection connection, ReadOnlySpan<byte> packetBuffer)
    {
        int offset = 0;
        BinaryReader reader = new(packetBuffer, ref offset);
        MobEquipmentPacket packet = (MobEquipmentPacket)Packet.Deserialize(reader);

        if (!SessionLookup.TryGetPlayer(server, connection, out global::Orion.Player.Player? player))
        {
            return;
        }

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
