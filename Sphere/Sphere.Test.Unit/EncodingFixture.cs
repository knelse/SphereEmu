using System.Text;

namespace Sphere.Test.Unit
{
    public class EncodingFixture
    {
        public EncodingFixture()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
    }
}
