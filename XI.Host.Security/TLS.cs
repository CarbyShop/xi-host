using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace XI.Host.Security
{
    public class TLS
    {
        public static readonly string LOGIN_KEY = "login.key";
        public static readonly string LOGIN_CERT = "login.cert";

        private static bool IsValid(NetworkInterface networkInterface)
        {
            return networkInterface.OperationalStatus == OperationalStatus.Up &&
                   (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                   networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) &&
                   !networkInterface.Description.ToLower().Contains("hyper-v");
        }

        private static string GetHostNameFromNetworkInterface()
        {
            string result = string.Empty;

            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (IsValid(networkInterface))
                {
                    foreach (var unicastAddress in networkInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (unicastAddress.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            result = unicastAddress.Address.ToString();
                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(result))
                    {
                        break;
                    }
                }
            }

            return result;
        }

        public static bool TryCreate(string loginAuthIp, bool overwrite = false)
        {
            bool result = true;

            if (!overwrite)
            {
                if (File.Exists(LOGIN_KEY) && File.Exists(LOGIN_CERT))
                {
                    return result;
                }
            }

            RSA? rsa = null;

            try
            {
                rsa = RSA.Create(4096);

                if (IPAddress.Parse(loginAuthIp).Equals(IPAddress.Any))
                {
                    loginAuthIp = GetHostNameFromNetworkInterface();
                }

                string fqdn = Dns.GetHostEntry(IPAddress.Parse(loginAuthIp)).HostName;
                string organization = "XI.Host self-signed certificate for login server";
                var subjectName = new X500DistinguishedName($"CN={fqdn}, O={organization}"); // , C=US

                var request = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA384, RSASignaturePadding.Pkcs1);

                // Add extensions (optional)
                //request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
                //request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

                var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(20));

                byte[] pkcs8 = rsa.ExportPkcs8PrivateKey();
                string key = Convert.ToBase64String(pkcs8, Base64FormattingOptions.InsertLineBreaks);
                string contents = $"-----BEGIN PRIVATE KEY-----\n{key}\n-----END PRIVATE KEY-----";

                File.WriteAllText(LOGIN_KEY, contents);

                string certificate = Convert.ToBase64String(cert.RawData, Base64FormattingOptions.InsertLineBreaks);
                contents = $"-----BEGIN CERTIFICATE-----\n{certificate}\n-----END CERTIFICATE-----";

                File.WriteAllText(LOGIN_CERT, contents);
            }
            catch (Exception)
            {
                // TODO
                result = false;
            }
            finally
            {
                rsa?.Dispose();
            }

            return result;
        }
    }
}
