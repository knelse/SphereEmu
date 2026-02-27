namespace SphereHelpers.Extensions;

public static class ArrayExtensions
{
    public static bool HasEqualElementsAs (this byte[] what, byte[] to, int fromIndex = 0)
    {
        if (to.Length > what.Length - fromIndex)
        {
            return false;
        }

        for (var i = 0; i < to.Length; i++)
        {
            if (what[i + fromIndex] != to[i])
            {
                return false;
            }
        }

        return true;
    }
}