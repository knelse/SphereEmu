using System;
using System.Collections.Generic;
using System.Linq;
using BitStreams;
using SphServer.Packets;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;
using static SphServer.Shared.BitStream.SphBitStream;

namespace SphServer.Client.Networking.Handlers.InGame.Mutator;

public enum MutatorType
{
    Default,
    Special
}

public enum SpecialMutator : ushort
{
    Магический_Свет = 1000,
    Берсерк = 1001,
    Видеть_Невидимое = 1002,
    Гнев_Скрижали = 1003,
    МО = 1004,
    Яд = 1005,
    Реген = 1006,
    Яд_Хп_Прана = 1007,
    Реген_Хп_Прана = 1008,
    ПО = 1009,
    Пятка = 1010,
    Квадрат = 1011,
    Нарушение_Координации = 1012,
    Запрет_Прыгать = 1013,
    Прыг_х2 = 1014,
    Прыг_х4 = 1015,
    Ослепление_Наполовину = 1016,
    Полное_Ослепление = 1017,
    Рыбка = 1018,
    Перо = 1019,
    Снять_Прыг = 1020,
    Проклятие_Наатх = 1021,
    Видеть_Ловушки = 1022,
    Инвиз = 1023,
    Снять_2_Мута = 1024,
    Снять_4_Мута = 1025,
    Снять_6_Мутов = 1026,
    ДС = 1027,
    СХ = 1028,
    Воровство = 1029,
    Инвиз_Ночью = 1030,
    Камае = 1031,
    АПО = 1032,
    Бонус_Дроп = 1033,
    Даль = 1034,
    Бег_Времени = 1035,
    Оковы = 1036,
    Близорукость = 1037,
    Медитация = 1038,
    Свинка = 1039,
    Оглушение = 1040,
    Волк = 1041,
    Быстрый_Бег = 1042,
    Ржавые_Доспехи = 1043,
    Смерч = 1044,
    Берс_Варвара = 1045,
    Щит_Праны = 1046,
    Вампир = 1047,
    Снеговик = 1048,
    Зеркальце = 1049,
    Свобода = 1050,
    Непокобелимость = 1051, // не опечатка
    Поддержка_Банда = 1111,
    Защита_Ворот = 2020,
    Вынос_Ворот = 2021
}

public static class MutatorHandler
{
    /// <summary>
    ///     Live mutator_bag wire bytes (content[39..49]): type-specific blob before mutator_id.
    ///     Captured from retail; mutator_id alone is not enough for the correct icon.
    /// </summary>
    private static readonly Dictionary<SpecialMutator, byte[]> MutatorBagWire = new()
    {
        [SpecialMutator.Видеть_Невидимое] = Convert.FromHexString("44ED797B4E83000A0F07"), // 1002
        [SpecialMutator.МО] = Convert.FromHexString("44EDB9BC4D83200A0F07"), // 1004
        [SpecialMutator.Яд] = Convert.FromHexString("426D317CCE82400B0F07"), // 1005 (non-44ED family)
        [SpecialMutator.Прыг_х2] = Convert.FromHexString("44ED79984D83600A0F07"), // 1014
        [SpecialMutator.Прыг_х4] = Convert.FromHexString("44EDF9994D83C00A0F07"), // 1015
        [SpecialMutator.Рыбка] = Convert.FromHexString("44ED79D84C83800A0F07"), // 1018
        [SpecialMutator.Перо] = Convert.FromHexString("44EDB9D94C83000A0F07"), // 1019
        [SpecialMutator.СХ] = Convert.FromHexString("44EDF91C4E83E00B0F07"), // 1028
        [SpecialMutator.Свинка] = Convert.FromHexString("44ED79BB4C83A00A0F07"), // 1039
    };

    public static void SendSpecialMutator(ClientConnection clientConnection, ushort localId, SpecialMutator mutator,
        BelongingSlot slot)
    {
        var mutatorId = (ushort)mutator;
        var itemId = WorldObjectIndex.New();
        var playerId = ByteSwap(localId);

        var reserveParts = PacketPart.LoadDefinedWithOverride("new_item_reserve_slot_full");
        PacketPart.UpdateEntityId(reserveParts, playerId);
        PacketPart.UpdateValue(reserveParts, "slot_id", (int)slot, 8);
        PacketPart.UpdateValue(reserveParts, "new_item_id", itemId, 16);
        clientConnection.MaybeScheduleNetworkPacketSend(PacketPart.GetBytesToWrite(reserveParts));

        var mutatorParts = PacketPart.LoadDefinedPartsFromFile(ObjectType.Mutator);
        PacketPart.UpdateEntityId(mutatorParts, itemId);
        PacketPart.UpdateValue(mutatorParts, "object_type", (int)ObjectType.Mutator, 10);
        PacketPart.UpdateValue(mutatorParts, "to_id", playerId, 16);
        ApplyMutatorBag(mutatorParts, mutator);
        PacketPart.UpdateValue(mutatorParts, "mutator_id", mutatorId, 16);
        PacketPart.UpdateValue(mutatorParts, "from_id", playerId, 16);
        clientConnection.MaybeScheduleNetworkPacketSend(PacketPart.GetBytesToWrite(mutatorParts));

        SphLogger.Info($"Sent mutator {mutator} ({mutatorId}) to slot {slot} as {itemId:X4}. Client ID: {localId:X4}");
    }

    private static void ApplyMutatorBag(List<PacketPart> parts, SpecialMutator mutator)
    {
        var bagPart = parts.FirstOrDefault(p => p.Name == "mutator_bag");
        if (bagPart is null)
        {
            return;
        }

        if (!MutatorBagWire.TryGetValue(mutator, out var wire))
        {
            SphLogger.Info($"No mutator_bag template for {mutator}; using definition default");
            return;
        }

        var bits = new List<Bit>(wire.Length * 8);
        foreach (var b in wire)
        {
            for (var i = 0; i < 8; i++)
            {
                bits.Add((b >> i) & 1);
            }
        }

        bagPart.Value = bits;
        bagPart.BitLength = bits.Count;
    }
}
