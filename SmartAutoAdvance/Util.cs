using System.Collections.Generic;
using System.Text;

namespace SmartAutoAdvance {
    public static class Util {
        private static unsafe byte[] ReadTerminatedBytes(byte* ptr) {
            if (ptr == null) {
                return System.Array.Empty<byte>();
            }

            var bytes = new List<byte>();
            while (*ptr != 0) {
                bytes.Add(*ptr);
                ptr += 1;
            }

            return bytes.ToArray();
        }

        public static unsafe string ReadTerminatedString(byte* ptr) {
            return Encoding.UTF8.GetString(ReadTerminatedBytes(ptr));
        }
    }
}
