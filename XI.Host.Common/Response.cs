using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace XI.Host.Common
{
    /// <summary>
    /// Not using this interface for the purpose of abstraction, rather to make sure certain things are defined. 
    /// </summary>
    public interface IResponse
    {
        void Append(in byte value);
        void Append(in ushort value);
        void Append(in uint value);
        void Append(in ulong value);
        void Append(string value, in int fixedLength);
        void Append(ReadOnlySpan<byte> data, in int fixedLength);
        void Append(ReadOnlySpan<byte> data);
        void Pad(in int amount);
        byte[] GetBytes(in uint fixedLength);
        byte[] GetBytes();
    }

    /// <summary>
    /// Lean-and-mean; don't add any error checking so that callers will snag the errors and figure out what to do
    /// at their own level.
    /// </summary>
    public abstract class Response : IDisposable
    {
        protected MemoryStream ms;

        public Response()
        {
            ms = new MemoryStream();
        }

        public void Append(in byte value)
        {
            ms.WriteByte(value);
        }

        public void Append(in ushort value)
        {
            ms.Write(BitConverter.GetBytes(value));
        }

        public void Append(in uint value)
        {
            ms.Write(BitConverter.GetBytes(value));
        }

        public void Append(in ulong value)
        {
            ms.Write(BitConverter.GetBytes(value));
        }

        public void Append(string value, in int fixedLength)
        {
            ms.Write(Encoding.UTF8.GetBytes(value));
            Pad(fixedLength - value.Length);
        }

        public void Append(ReadOnlySpan<byte> data, in int fixedLength)
        {
            ms.Write(data);
            Pad(fixedLength - data.Length);
        }

        public void Append(ReadOnlySpan<byte> data)
        {
            // Assume data != null && data.Length > 0, MemoryStream.Write should handle this case.  If not, calling
            // code is bad anyway and we want to know via an exception.
            ms.Write(data);
        }

        /// <summary>
        /// Pads the buffer with the specified amount of zeros (in bytes).
        /// </summary>
        /// <param name="amount"></param>
        public void Pad(in int amount)
        {
            // Assume amount > 0, MemoryStream.Write should handle this case.  If not, calling code is bad anyway
            // and we want to know via an exception.
            ms.Write(new byte[amount]); // .NET automatically zeros these bytes.
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    //-TODO: dispose managed state (managed objects).
                    ms?.Dispose();
                }

                //-TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                //-TODO: set large fields to null.

                disposedValue = true;
            }
        }

        //-TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~Response()
        // {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            //-TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion
    }

    public sealed class AuthenticationResponse : Response, IResponse
    {
        public static class Codes
        {
            public static readonly byte SUCCEED = 0x01;
            public static readonly byte FAIL = 0x02;
            public static readonly byte CREATE_SUCCEED = 0x03;
            public static readonly byte CREATE_FAIL_TAKEN = 0x04;
            public static readonly byte CHANGE_PASSWORD = 0x05;
            public static readonly byte CHANGE_PASSWORD_SUCCEED = 0x06;
            public static readonly byte CHANGE_PASSWORD_FAIL = 0x07;
            public static readonly byte CREATE_DISABLED = 0x08;
            public static readonly byte CREATE_FAIL = 0x09;
            public static readonly byte CREATE_WARNING_LOCKOUT = 0x44; // DNE in xiloader, will silently close the connection.
            public static readonly byte WAIT = 0x75;
            public static readonly byte INVALID = 0x76;
            public static readonly byte TOO_MANY = 0x77;
        }

        public AuthenticationResponse() { }

        public AuthenticationResponse(in byte code)
        {
            ms.WriteByte(code);
        }

        public byte[] GetBytes(in uint fixedLength)
        {
            // Just let callers know this is not the correct interface method.
            throw new NotSupportedException();
        }

        public byte[] GetBytes()
        {
            return ms.ToArray();
        }
    }

    public sealed class ViewResponse : Response, IResponse
    {
        private static readonly object computeSynchronizer = new object();
        private static readonly MD5 md5 = MD5.Create();

        public static class Codes
        {
            public static readonly uint SUCCESS = 0x03;
            public static readonly uint ERROR = 0x04;
            public static readonly uint VERSION = 0x05;
            public static readonly uint SELECTION = 0x0B;
            public static readonly uint CHARACTERS = 0x20;
            public static readonly uint SERVERS = 0x23;
        }

        public static class Errors
        {
            public static readonly uint UNSUPPORTED_PROTOCOL = 300;
            public static readonly uint COMMUNICATIONS_ERROR_LOGOUT = 301;
            public static readonly uint AUTH_FAIL_WRONG_PASSWORD = 302;
            public static readonly uint CHARACTER_NOT_FOUND = 303;
            public static readonly uint CONTENT_ID_NOT_FOUND = 304;
            public static readonly uint UNABLE_TO_CONNECT_WORLD_SERVER = 305;
            public static readonly uint LOGIN_SEARCH_SERVER_ERROR = 306;
            public static readonly uint CHARACTER_ALREADY_LOGGED_IN = 307;
            public static readonly uint WORLD_SERVER_MAINTENANCE = 308;
            public static readonly uint LOBBY_SERVER_REGISTRATION_FAIL = 309;
            public static readonly uint REGISTRATION_1_FAILED = 310;
            public static readonly uint REGISTRATION_2_FAILED = 311;
            public static readonly uint DELETE_FAILED_1 = 312;
            // XI-3313 translation from Japanese: The name you entered can't be registered because it is already in use. Please try a different name.
            public static readonly uint NAME_TAKEN = 313;
            public static readonly uint NAME_SERVER_REGISTRATION_FAILED = 314;
            public static readonly uint INTERNAL_ERROR_1 = 315;
            public static readonly uint INCORRECT_GOLD_WORLD_PASS = 316;
            public static readonly uint GOLD_WORLD_PASS_EXPIRED = 317;
            public static readonly uint DELETE_FAILED_2 = 318;
            public static readonly uint INTERNAL_ERROR_2 = 319;
            public static readonly uint INTERNAL_ERROR_3 = 320;
            public static readonly uint CHARACTER_PARAMS_INCORRECT = 321;
            // XI-3322 translation from Japanese: The name you have entered can not be recorded, as it may already be in use (or other issues).  Please enter another name.
            public static readonly uint JAPANESE_2 = 322;
            public static readonly uint ERROR_NUMBER_ONLY = 323;
            public static readonly uint GOLD_WORLD_PASS_INVALID_FOR_WORLD = 324;
            public static readonly uint INTERNAL_ERROR_4_TRY_CHAR_CREATE_AGAIN = 325;
            public static readonly uint CHARACTER_RESERVATION_CANCEL_FAIL = 326;
            public static readonly uint SERVER_POPULATION_LIMIT_REACHED = 327; // Use if/when concurrent users reaches X value.

            public static readonly uint GAME_DATA_UPDATED_DOWNLOAD_LATEST = 331;

            public static readonly uint REGISTRATION_3_FAILED = 336;
        }

        public static class Constants
        {
            public const int VERSION_OFFSET = 0x74;

            public static readonly ushort GEAR_OFFSET_0x10 = 0x1000;
            public static readonly ushort GEAR_OFFSET_0x20 = 0x2000;
            public static readonly ushort GEAR_OFFSET_0x30 = 0x3000;
            public static readonly ushort GEAR_OFFSET_0x40 = 0x4000;
            public static readonly ushort GEAR_OFFSET_0x50 = 0x5000;
            public static readonly ushort GEAR_OFFSET_0x60 = 0x6000;
            public static readonly ushort GEAR_OFFSET_0x70 = 0x7000;

            public static readonly byte ACTIVE = 0x01;
            public static readonly byte INACTIVE = 0x02;

            public static readonly byte KEEP_NAME = 0x00;
            public static readonly byte CHANGE_NAME = 0x01;

            // Wireshark captures show random values.  May be partial MD5 (just the first 4 bytes), but the client does not
            // care.  Why would it...?
            public static readonly byte[] VERSION_HEADER = new byte[] { 0x4f, 0xe0, 0x5d, 0xad };

            public static readonly byte[] SELECTION_HEADER = new byte[] { 0x82, 0xB2, 0xc0, 0x00, 0xC3, 0x57, 0x00, 0x00 };
            public static readonly byte[] IP_PORT_COMBO_COUNT = new byte[] { 0x02, 0x00, 0x00, 0x00 };
            public static readonly byte[] UNKNOWN_DATA_4 = new byte[] { 0x01, 0x00, 0x02, 0x00 };
            public static readonly byte[] UNKNOWN_DATA_6 = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
            public static readonly byte[] UNKNOWN_DATA_7 = new byte[] { 0x07, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00 };
            public static readonly byte[] UNKNOWN_DATA_12 = new byte[] { 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
            public static readonly byte[] SERVERS_HEADER = new byte[] { 0x20, 0x00, 0x00, 0x00 };
            public static readonly byte[] EMPTY_NAME = new byte[] { 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            public static readonly byte[] PROBABLE_POL_DATA = new byte[] { 0x00, 0x00, 0xB5, 0xFA, 0x01, 0x00, 0x7E, 0x00, 0x00, 0x00, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x46, 0x6e, 0xcf, 0x09, 0xde, 0x17, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0a, 0x52, 0x03, 0x00, 0x0e, 0x08, 0x00, 0x00, 0x00, 0x0f, 0x00, 0x00 };
        }

        //private static readonly uint IXFF = 1779015241;
        private static readonly int MD5_LENGTH = 16;
        private static readonly int MD5_OFFSET = 12;
        private static readonly byte[] LENGTH_PLACEHOLDER_BYTES = BitConverter.GetBytes(uint.MinValue);
        private static readonly byte[] IXFF_CONSTANT_BYTES = Encoding.UTF8.GetBytes("IXFF"); // BitConverter.GetBytes(IXFF);
        private static readonly byte[] MD5_PLACEHOLDER_BYTES = new byte[MD5_LENGTH]; // .NET automatically zeros these bytes.

        public ViewResponse() : this(Codes.SUCCESS) { }

        public ViewResponse(in uint code) : base()
        {
            ms.Write(LENGTH_PLACEHOLDER_BYTES); // Length placeholder
            ms.Write(IXFF_CONSTANT_BYTES);
            ms.Write(BitConverter.GetBytes(code));
            ms.Write(MD5_PLACEHOLDER_BYTES); // MD5 placeholder
        }

        public byte[] GetBytes(in uint fixedLength)
        {
            // Just let callers know this is not the correct interface method.
            throw new NotSupportedException();
        }

        public byte[] GetBytes()
        {
            byte[] result = ms.ToArray();

            // Replace the length with actual.
            byte[] lengthBytes = BitConverter.GetBytes(result.Length);
            Array.Copy(lengthBytes, result, lengthBytes.Length);
            
            lock (computeSynchronizer)
            {
                // Compute and insert the MD5.
                byte[] md5Bytes = md5.ComputeHash(result);
                Array.Copy(md5Bytes, 0, result, MD5_OFFSET, md5Bytes.Length);
            }

            return result;
        }
    }

    public sealed class DataResponse : Response, IResponse
    {
        public static class Codes
        {
            public static readonly byte READY = 0x01;
            public static readonly byte SET = 0x02;
            public static readonly byte LIST = 0x03;
        }

        public static class Constants
        {
            // Question: Why static readonly vs. const?
            // Answer:   static readonly can be set from run-time values read from a configuration, const can never be
            //           changed once declared.  Bottom line; flexibility.
            // Source:   https://stackoverflow.com/questions/755685/static-readonly-vs-const
            public static readonly byte[] WHO_ARE_YOU = new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00 };
        }

        public DataResponse(in byte code) : base()
        {
            ms.WriteByte(code);
        }

        public DataResponse(in byte code, in byte count) : base()
        {
            ms.WriteByte(code);
            ms.WriteByte(count);
        }

        public byte[] GetBytes(in uint fixedLength)
        {
            long padding = fixedLength - ms.Length;

            if (padding > 0)
            {
                ms.Write(new byte[padding]);
            }

            return ms.ToArray();
        }

        public byte[] GetBytes()
        {
            return ms.ToArray();
        }
    }
}
