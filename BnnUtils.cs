// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Binance.Common;
using Microsoft.Extensions.Logging;

namespace Bnncmd
{
    internal class BnnUtils
    {
        public static bool IsTest { get; set; }

        public static decimal FormatQuantity(decimal availableSum, double precise)
        {
            var log10 = Math.Log10(precise);
            double power = Math.Pow(10, -log10);			
            return (decimal)(Math.Floor((double)availableSum * power) / power); // to prevent LOT_SIZE error
        }

        public static HttpClient BuildLoggingClient()
        {
            ILoggerFactory loggerFactory;
            loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
            });
            ILogger logger = loggerFactory.CreateLogger<Program>();

            HttpMessageHandler loggingHandler = new BinanceLoggingHandler(logger: logger);
            return new HttpClient(handler: loggingHandler);
        }

        public static void ClearCurrentConsoleLine()
        {
            var currentLineCursor = Console.CursorTop;
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, currentLineCursor);
        }

        public static string FormatUnixTime(long unixTimeStamp, bool isMinues = true)
        {
            return UnitTimeToDateTime(unixTimeStamp).ToString(isMinues ? "dd.MM.yyyy HH:mm:ss" : "dd.MM.yyyy");
        }


        public static DateTime UnitTimeToDateTime(long unixTimeStamp)
        {
            var dt = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            return dt.AddSeconds(unixTimeStamp / 1000).ToLocalTime();
        }


        public static long DateTimeToUnitTime(DateTime dateTime, bool milliseconds = true)
        {
            return ((DateTimeOffset)dateTime).ToUnixTimeMilliseconds();
        }


        public static long GetUnixNow(bool milliseconds = true)
        {
            if (milliseconds) return ((DateTimeOffset)DateTime.Now).ToUnixTimeMilliseconds();
            else return ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
        }


        public static string GetPublicIp()
        {
            HttpClient client = new();
            return client.GetStringAsync("https://api.ipify.org").Result; // http://icanhazip.com -- System.Net. await 
                                                                          // return new System.Net.WebClient().DownloadString("https://api.ipify.org"); // http://icanhazip.com
        }

        private static bool _lastLogLineIsEmpty = false;

        public static bool LastLogLineIsEmpty()
        {
            return _lastLogLineIsEmpty;
        }

        public static void Log(string message = "", bool addDateTime = true)
        {
            _lastLogLineIsEmpty = message == string.Empty;
            if (addDateTime) message = $"{DateTime.Now:dd.MM.yyyy HH:mm:ss} " + message;
            if (!IsTest)
            {
                var fileName = $"{DateTime.Now:yyyy-MM-dd}.txt";
                File.AppendAllText(fileName, message + Environment.NewLine);
            }
            ClearCurrentConsoleLine(); // current price info
            Console.WriteLine(message);
        }
    }

    public class Crypto
    {

        //While an app specific salt is not the best practice for
        //password based encryption, it's probably safe enough as long as
        //it is truly uncommon. Also too much work to alter this answer otherwise.
        private static readonly byte[] s_salt = [9, 8, 5, 25, 14, 47, 89, 10, 7]; // __To_Do__("Add a app specific salt here");

        /// <summary>
        /// Encrypt the given string using AES.  The string can be decrypted using 
        /// DecryptStringAES().  The sharedSecret parameters must match.
        /// </summary>
        /// <param name="plainText">The text to encrypt.</param>
        /// <param name="sharedSecret">A password used to generate a key for encryption.</param>
        public static string EncryptStringAES(string plainText, string sharedSecret)
        {
            if (string.IsNullOrEmpty(plainText)) throw new ArgumentNullException(nameof(plainText));
            if (string.IsNullOrEmpty(sharedSecret)) throw new ArgumentNullException(nameof(sharedSecret));

            string? outStr = null;                       // Encrypted string to return
            // RijndaelManaged? aesAlg = null;              // RijndaelManaged object used to encrypt the data.


            // generate the key from the shared secret and the salt
            var key = new Rfc2898DeriveBytes(sharedSecret, s_salt, 1000, HashAlgorithmName.SHA1); // , 100000, HashAlgorithmName.SHA512

            // Create a RijndaelManaged object
            var aesAlg = Aes.Create(); // "AesManaged"
            // aesAlg = new RijndaelManaged();
            aesAlg.Key = key.GetBytes(aesAlg.KeySize / 8);

            // Create a decryptor to perform the stream transform.
            var encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

            // Create the streams used for encryption.
            using (var msEncrypt = new MemoryStream())
            {
                // prepend the IV
                msEncrypt.Write(BitConverter.GetBytes(aesAlg.IV.Length), 0, sizeof(int));
                msEncrypt.Write(aesAlg.IV, 0, aesAlg.IV.Length);
                using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                {
                    using var swEncrypt = new StreamWriter(csEncrypt);
                    swEncrypt.Write(plainText);
                }
                outStr = Convert.ToBase64String(msEncrypt.ToArray());
            }

            // Return the encrypted bytes from the memory stream.
            return outStr;
        }

        /// <summary>
        /// Decrypt the given string.  Assumes the string was encrypted using 
        /// EncryptStringAES(), using an identical sharedSecret.
        /// </summary>
        /// <param name="cipherText">The text to decrypt.</param>
        /// <param name="sharedSecret">A password used to generate a key for decryption.</param>
        public static string DecryptStringAES(string cipherText, string sharedSecret)
        {
            if (string.IsNullOrEmpty(cipherText)) throw new ArgumentNullException(nameof(cipherText));
            if (string.IsNullOrEmpty(sharedSecret)) throw new ArgumentNullException(nameof(sharedSecret));

            // Declare the RijndaelManaged object
            // used to decrypt the data.
            // RijndaelManaged? aesAlg = null;

            // Declare the string used to hold
            // the decrypted text.
            string? plaintext = null;

            // generate the key from the shared secret and the salt
            var key = new Rfc2898DeriveBytes(sharedSecret, s_salt, 1000, HashAlgorithmName.SHA1); // , 100000, HashAlgorithmName.SHA512

            // Create the streams used for decryption.                
            var bytes = Convert.FromBase64String(cipherText);
            using var msDecrypt = new MemoryStream(bytes);
            // Create a RijndaelManaged object
            // with the specified key and IV.
            // var aesAlg = new RijndaelManaged();
            var aesAlg = Aes.Create(); // "AesManaged"
            aesAlg.Key = key.GetBytes(aesAlg.KeySize / 8);
            // Get the initialization vector from the encrypted stream
            aesAlg.IV = ReadByteArray(msDecrypt);
            // Create a decrytor to perform the stream transform.
            var decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);
            using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
            {
                using var srDecrypt = new StreamReader(csDecrypt);
                // Read the decrypted bytes from the decrypting stream
                // and place them in a string.
                plaintext = srDecrypt.ReadToEnd();
            }


            return plaintext;
        }

        private static byte[] ReadByteArray(Stream s)
        {
            var rawLength = new byte[sizeof(int)];
            if (s.Read(rawLength, 0, rawLength.Length) != rawLength.Length)
            {
                throw new SystemException("Stream did not contain properly formatted byte array");
            }

            var buffer = new byte[BitConverter.ToInt32(rawLength, 0)];
            if (s.Read(buffer, 0, buffer.Length) != buffer.Length)
            {
                throw new SystemException("Did not read byte array properly");
            }

            return buffer;
        }
    }
}
