using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualBasic.FileIO;

namespace Hashnamer {
	class Program {
		struct IntHash : IEquatable<IntHash> {//represents an MD5 hash (which are always 128b) as a pair of UInt64s
			//value object; uses value comparisons
			UInt64 topHalf;
			UInt64 bottomHalf;
			public IntHash(byte[] md5Hash) {
				topHalf = BitConverter.ToUInt64(md5Hash, startIndex: 0);
				bottomHalf = BitConverter.ToUInt64(md5Hash, startIndex: 8);
			}

			public bool Equals(IntHash other) {
				return topHalf == other.topHalf && bottomHalf == other.bottomHalf;
			}

			public override string ToString() {//hex
				return String.Format("{0:X16}{1:X16}", topHalf, bottomHalf);
			}
		}

		static string GetHashname(string originalFilename, IntHash hash) {
			return Path.GetDirectoryName(originalFilename)+Path.DirectorySeparatorChar+hash.ToString()+Path.GetExtension(originalFilename);
		}

		//https://stackoverflow.com/questions/3670057/does-console-writeline-block
		public static class NonBlockingConsole {
			private static BlockingCollection<string> queue = new BlockingCollection<string>();

			static NonBlockingConsole() {
				var thread = new Thread(
					() => {	while(true) Console.WriteLine(queue.Take());
					}) {
					IsBackground = true
				};
				thread.Start();
			}

			public static void WriteLine(string value) {
				queue.Add(value);
			}
		}

		static void RenameFileToHashname(string filename, IntHash hash, Dictionary<string, IntHash> filenamesToFileHash) {
			string hashname = GetHashname(filename, hash);
			if(!string.Equals(filename, hashname, StringComparison.CurrentCultureIgnoreCase)) {
				if(File.Exists(hashname)) {
					RenameFileToHashname(hashname, filenamesToFileHash[hashname], filenamesToFileHash);
				}
				File.Move(filename, hashname);
				NonBlockingConsole.WriteLine(String.Format("Renamed {0} to {1}", filename, hashname));
			} else {
				NonBlockingConsole.WriteLine(String.Format("{0} left alone; already has a hashname", filename));
			}
		}

		static void Main(string[] args) {
			if(args.Length == 0) {
				Console.WriteLine("Please enter a path to a folder to work on:");
				Array.Resize(ref args, 1);
				args[0] = Console.ReadLine();
			}

			for(int i = 0; i<args.Length; i++) {//work once on every path passed in
				{
					var attr = File.GetAttributes(args[i]);
					if((attr & FileAttributes.Directory) == FileAttributes.Directory) {
						NonBlockingConsole.WriteLine(args[i] + " is not a folder!");
						break;
					}
				}
				var filenames = Directory.EnumerateFiles(args[i]);
				//probably faster to maintain this to check for hash collisions than checking the directory every time
				var hashToOriginalFilenames = new Dictionary<IntHash, string>();

				NonBlockingConsole.WriteLine(String.Format("Working on folder {0}...", args[i]));

				foreach(string filename in filenames) {
					var file = File.OpenRead(filename);
					var md5Hash = new IntHash(MD5.Create().ComputeHash(file));
					file.Close();
					if(!hashToOriginalFilenames.ContainsKey(md5Hash)) {
						hashToOriginalFilenames[md5Hash] = filename;
					} else {//collision!
						NonBlockingConsole.WriteLine(String.Format("Dupe detected! {0} hashed as a duplicate of {1} with an MD5 of {2}. Deleting duplicate...", filename, hashToOriginalFilenames[md5Hash], md5Hash));
						FileSystem.DeleteFile(
							filename,
							UIOption.OnlyErrorDialogs,
							RecycleOption.SendToRecycleBin,
							UICancelOption.ThrowException
						);
					}
				}
				NonBlockingConsole.WriteLine("Dupes removed in " + args[i] + ", renaming files...");
				var filenamesToFileHash = new Dictionary<string, IntHash>();
				foreach(var hashAndFilename in hashToOriginalFilenames) {
					filenamesToFileHash[hashAndFilename.Value] = hashAndFilename.Key;
				}
				foreach(var hashAndFilename in hashToOriginalFilenames) {
					RenameFileToHashname(hashAndFilename.Value, hashAndFilename.Key, filenamesToFileHash);
				}

				NonBlockingConsole.WriteLine("Done with "+args[i]);
			}
		}
	}
}

