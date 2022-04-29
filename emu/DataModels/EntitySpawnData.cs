using System.Text;
using emu.Helpers;

namespace emu.DataModels;

public class EntitySpawnData
{
    public ushort ID;
    public ushort EntType;
    public uint X;
    public uint Y;
    public uint Z;
    public byte Angle;
    public ushort HP;
    public ushort ModelType;
    public byte Level;

    public override string ToString()
    {
        return $"{ID}\t{EntType}\t{X}\t{Y}\t{Z}\t{Angle}\t{HP}\t{ModelType}\t{Level}";
    }

    public byte[] ToByteArray()
    {
        var sb = new StringBuilder();
        sb.Append("11111100");
        sb.Append("11010010");
        sb.Append("00111001");
        sb.Append("01110000");
        sb.Append("00000000");
        sb.Append("11000000");
        var id_str = ID.ToBinaryString();
        sb.Append(id_str[12..]);
        sb.Append("1111");
        sb.Append(id_str[4..12]);
        var enttype_str = EntType.ToBinaryString();
        sb.Append(enttype_str[12..]);
        sb.Append(id_str[..4]);
        sb.Append(enttype_str[4..12]);
        sb.Append("1111");
        sb.Append(enttype_str[..4]);
        var x_str = X.ToBinaryString();
        sb.Append(x_str[26..]);
        sb.Append("01");
        sb.Append(x_str[18..26]);
        sb.Append(x_str[10..18]);
        sb.Append(x_str[2..10]);
        var y_str = Y.ToBinaryString();
        sb.Append(y_str[26..]);
        sb.Append(x_str[..2]);
        sb.Append(y_str[18..26]);
        sb.Append(y_str[10..18]);
        sb.Append(y_str[2..10]);
        var z_str = Z.ToBinaryString();
        sb.Append(z_str[26..]);
        sb.Append(y_str[..2]);
        sb.Append(z_str[18..26]);
        sb.Append(z_str[10..18]);
        sb.Append(z_str[2..10]);
        var angle_str = Angle.ToBinaryString();
        sb.Append(angle_str[2..]);
        sb.Append(z_str[..2]);
        var hp_str = HP.ToBinaryString();
        sb.Append(hp_str[14..]);
        sb.Append("0001");
        sb.Append(angle_str[..2]);
        sb.Append(hp_str[6..14]);
        sb.Append("10");
        sb.Append(hp_str[..6]);
        sb.Append("11111000");
        sb.Append("00000001");
        var modeltype_str = ModelType.ToBinaryString();
        sb.Append(modeltype_str[8..]);
        sb.Append(modeltype_str[..8]);
        var level_str = Level.ToBinaryString();
        sb.Append(level_str[2..]);
        sb.Append("11");
        sb.Append("111101");
        sb.Append(level_str[..2]);
        sb.Append("00000001");

        return BitHelper.BinaryStringToByteArray(sb.ToString());
    }
}