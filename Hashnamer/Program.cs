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
			private static BlockingCollection<string> writeQueue = new BlockingCollection<string>();
			private static BlockingCollection<string> titleQueue = new BlockingCollection<string>();

			static NonBlockingConsole() {
				var writeThread = new Thread(
					() => {	while(true) {Console.WriteLine(writeQueue.Take());};
					}) {
					IsBackground = true
				};

				var titleThread = new Thread(
					() => {
						while(true) {Console.Title = titleQueue.Take();};
					}) {
					IsBackground = true
				};

				writeThread.Start();
				titleThread.Start();
			}

			public static void WriteLine(string value) {
				writeQueue.Add(value);
			}
			
			public static string Title {
				set { titleQueue.Add(value); }
			}

		}

		static void RenameFileToHashname(string filename, IntHash hash, Dictionary<string, IntHash> filenamesToFileHash) {
			string hashname = GetHashname(filename, hash);
			if(!string.Equals(filename, hashname, StringComparison.CurrentCultureIgnoreCase)) {
				if(File.Exists(hashname)) {
					RenameFileToHashname(hashname, filenamesToFileHash[hashname], filenamesToFileHash);
				}
				File.Move(filename, hashname);
				NonBlockingConsole.Title = String.Format("Renamed {0} to {1}", filename, hashname);
			} else {
				NonBlockingConsole.Title = String.Format("{0} left alone; already has a hashname", filename);
			}
		}

		static void SantizeFolderPaths(ref List<string> folderPaths) {
			folderPaths.RemoveAll((string folderPath) => {
				try {
					Path.GetFullPath(folderPath);
				} catch {
					Console.WriteLine(folderPath + " is not a valid path!");
					return true;
				}
				if(!Directory.Exists(folderPath)) {
					Console.WriteLine("Could not find " + folderPath);
					return true;
				}
				if((File.GetAttributes(folderPath) & FileAttributes.Directory) != FileAttributes.Directory) {
					Console.WriteLine(folderPath + " is not a folder!");
					return true;
				};
				return false;
			});
		}

		static void Main(string[] args) {
			List<string> folderPaths = new List<string>(args);

			SantizeFolderPaths(ref folderPaths);

			if(folderPaths.Count == 0) {
				Console.WriteLine("Please enter a path to a folder to work on:");
				folderPaths.Add(Console.ReadLine().Trim('\"'));
				SantizeFolderPaths(ref folderPaths);
			}

			if(folderPaths.Count == 0) {
				Console.WriteLine("Please enter a valid path to a folder to work on:");
				folderPaths.Add(Console.ReadLine().Trim('\"'));
				SantizeFolderPaths(ref folderPaths);
			}

			if(folderPaths.Count == 0) {
				Console.WriteLine("Exiting...");
			} else {
				foreach(string folderPath in folderPaths) {//work once on every path passed in
					RenameFilesinFolderWithHash(folderPath);
				}
			}
		}

		private static void RenameFilesinFolderWithHash(string folderPath) {
			var filenames = Directory.EnumerateFiles(folderPath);
			//probably faster to maintain this to check for hash collisions than checking the directory every time

			NonBlockingConsole.WriteLine(String.Format("Working on folder {0}...", folderPath));

			var hashToOriginalFilenames = HashFiles(new List<string>(filenames));

			NonBlockingConsole.WriteLine("No dupes in " + folderPath + ", renaming files...");
			var filenamesToFileHash = new Dictionary<string, IntHash>();
			foreach(var hashAndFilename in hashToOriginalFilenames) {
				filenamesToFileHash[hashAndFilename.Value] = hashAndFilename.Key;
			}
			foreach(var hashAndFilename in hashToOriginalFilenames) {
				RenameFileToHashname(hashAndFilename.Value, hashAndFilename.Key, filenamesToFileHash);
			}

			NonBlockingConsole.WriteLine("Done with " + folderPath);
		}

		private static Dictionary<IntHash, string> HashFiles(List<string> filenames) {
			var hashToOriginalFilenames = new Dictionary<IntHash, string>();

			var fileCount = filenames.Count;

			for(int i = 0; i<fileCount; i++) {
				NonBlockingConsole.Title = String.Format("{0}% done, now processing {1}", i*100/fileCount, filenames[i]);
				HashAndDedupeFile(ref hashToOriginalFilenames, filenames[i]);
			}

			return hashToOriginalFilenames;
		}

		private static void HashAndDedupeFile(ref Dictionary<IntHash, string> hashToOriginalFilenames, string filename) {
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
	}
}

