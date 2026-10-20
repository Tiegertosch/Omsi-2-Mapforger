using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiTextFileTests
    {
        [Test]
        public void DecodesUtf16LittleEndianWithByteOrderMark()
        {
            var bytes = Combine(new byte[] { 0xFF, 0xFE }, Encoding.Unicode.GetBytes("[name]\r\nStadtückheim"));

            Assert.AreEqual("[name]\r\nStadtückheim", OmsiTextFile.Decode(bytes));
        }

        [Test]
        public void DecodesUtf8WithoutByteOrderMark()
        {
            Assert.AreEqual("Grüße", OmsiTextFile.Decode(Encoding.UTF8.GetBytes("Grüße")));
        }

        [Test]
        public void StripsUtf8ByteOrderMark()
        {
            var bytes = Combine(new byte[] { 0xEF, 0xBB, 0xBF }, Encoding.UTF8.GetBytes("[map]"));

            Assert.AreEqual("[map]", OmsiTextFile.Decode(bytes));
        }

        [Test]
        public void FallsBackToWindows1252ForInvalidUtf8()
        {
            // "Straße 5€" in Windows-1252: ß = 0xDF, € = 0x80.
            var bytes = new byte[] { 0x53, 0x74, 0x72, 0x61, 0xDF, 0x65, 0x20, 0x35, 0x80 };

            Assert.AreEqual("Straße 5€", OmsiTextFile.Decode(bytes));
        }

        [Test]
        public void EncodeReproducesTheDecodedBytesInEveryEncoding()
        {
            const string text = "[name]\r\nStadtückheim €\r\n";
            foreach (OmsiTextEncoding encoding in System.Enum.GetValues(typeof(OmsiTextEncoding)))
            {
                var bytes = OmsiTextFile.Encode(text, encoding);

                Assert.AreEqual(text, OmsiTextFile.Decode(bytes, out var detected), encoding.ToString());
                Assert.AreEqual(encoding, detected);
            }
        }

        [Test]
        public void MapsUndefinedWindows1252BytesToControlCharactersReversibly()
        {
            var bytes = new byte[] { 0xC4, 0x81, 0x8D, 0x8F, 0x90, 0x9D };

            var text = OmsiTextFile.Decode(bytes, out var encoding);

            Assert.AreEqual(OmsiTextEncoding.Windows1252, encoding);
            Assert.AreEqual("Ä\u0081\u008D\u008F\u0090\u009D", text);
            CollectionAssert.AreEqual(bytes, OmsiTextFile.Encode(text, OmsiTextEncoding.Windows1252));
        }

        private static byte[] Combine(byte[] first, byte[] second)
        {
            var result = new byte[first.Length + second.Length];
            first.CopyTo(result, 0);
            second.CopyTo(result, first.Length);
            return result;
        }
    }
}
