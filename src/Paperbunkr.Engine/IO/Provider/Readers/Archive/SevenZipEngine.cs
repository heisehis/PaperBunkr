using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using cYo.Common.ComponentModel;
using cYo.Common.Compression.SevenZip;
using cYo.Common.IO;
using cYo.Common.Win32;
using Native = cYo.Projects.ComicRack.Engine.IO.Provider.Native;
using cYo.Common.Xml;
using cYo.Projects.ComicRack.Engine.IO.Provider.XmlInfo;

namespace cYo.Projects.ComicRack.Engine.IO.Provider.Readers.Archive
{
    public class SevenZipEngine : FileBasedAccessor
    {
        private const int SevenZipCheckSize = 131072;

        public static readonly string ConsoleExe32 = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Resources\\x86\\7z.exe");
        public static readonly string ConsoleExe64 = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Resources\\x64\\7z.exe");
        public static readonly string PackExe = Environment.Is64BitProcess ? ConsoleExe64 : ConsoleExe32; // Provides proper executable based on architecture.

        public static readonly string PackDll32 = Native.NativeInterop.ResolveNativeAssetPath(Assembly.GetExecutingAssembly(), "7z.dll");
        public static readonly string PackDll64 = Native.NativeInterop.ResolveNativeAssetPath(Assembly.GetExecutingAssembly(), "7z.dll");

        private static readonly Regex rxList = new Regex("Path = (?<filename>.*)\\r\\n(Folder.*\\r\\n)*?Size = (?<size>\\d+)", RegexOptions.Compiled);
        private static readonly Regex rxError = new Regex("Error:.+(^.+$)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.Compiled);

        private bool libraryMode;

        private static SevenZipFactory sevenZipFactory;
        private static SevenZipFactory SevenZipFactory => sevenZipFactory ??= new SevenZipFactory(Environment.Is64BitProcess ? PackDll64 : PackDll32);

        /// <summary>
        ///  Library mode is preferred for reading since it does not require spawning a separate process and does not have the overhead of starting a process.
        /// </summary>
        /// <param name="format">The format id from <see cref="KnownFileFormats"/></param>
        /// <param name="libraryMode">Will use the dll of 7-Zip instead of the executable</param>
        public SevenZipEngine(int format, bool libraryMode)
            : base(format)
        {

            this.libraryMode = libraryMode;
        }

        public override bool IsFormat(string source)
        {
            if (base.HasSignature)
            {
                return base.IsFormat(source);
            }
            try
            {
                IInArchive archive;
                using (OpenArchive(source, out archive))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public override IEnumerable<ProviderImageInfo> GetEntryList(string source)
        {
            if (libraryMode)
            {
                IInArchive archive;
                using (OpenArchive(source, out archive))
                {
                    int count = archive.GetNumberOfItems();
                    for (int i = 0; i < count; i++)
                    {
                        PropVariant value = default(PropVariant);
                        PropVariant value2 = default(PropVariant);
                        archive.GetProperty(i, ItemPropId.kpidPath, ref value);
                        archive.GetProperty(i, ItemPropId.kpidSize, ref value2);
                        yield return new ProviderImageInfo(i, value.GetObject().ToString(), value2.longValue);
                    }
                }
                yield break;
            }
            ExecuteProcess.Result result = ExecuteProcess.Execute(PackExe, "l -slt \"" + FileMethods.GetShortName(source) + "\"", ExecuteProcess.Options.StoreOutput);
            MatchCollection source2 = rxList.Matches(result.ConsoleText);
            foreach (ProviderImageInfo item in from m in source2.OfType<Match>()
                                               select new ProviderImageInfo(0, m.Groups["filename"].Value, long.Parse(m.Groups["size"].Value)))
            {
                yield return item;
            }
        }

        public override byte[] ReadByteImage(string source, ProviderImageInfo info)
        {
            return GetFileData(source, info);
        }

        // --- Keep-open reading session (docs/superpowers/specs/2026-09-08-reader-decode-cache-
        // prefetch-pipeline-design.md §4). Only the library-mode (7z.dll) path is stateful enough
        // to hold open; the console-exe path spawns a process per call and gains nothing.
        public override bool SupportsSession => libraryMode;

        public override IComicAccessorSession OpenSession(string source)
            => libraryMode ? SevenZipAccessorSession.TryOpen(this, source) : null;

        /// <summary>
        /// Holds one 7z.dll <see cref="IInArchive"/> open for a reading session. Non-solid
        /// containers (every <c>.cbz</c>) get cheap any-order reads; solid <c>.cb7</c>/<c>.cbr</c>
        /// get a forward-mark range extract (§4.2) so a sequential read pays the solid-block
        /// decompression cost once, not per page. Not thread-safe (per <see cref="IComicAccessorSession"/>).
        /// </summary>
        /// <summary>Last <see cref="SevenZipAccessorSession"/> open failure, for diagnostics - a failed session open is non-fatal (§4.5: the pipeline falls back to the stateless read path).</summary>
        internal static System.Exception LastSessionOpenError { get; private set; }

        /// <summary>
        /// Test seam (rev-3 addendum, design §15 #1): invoked with <see cref="Environment.CurrentManagedThreadId"/>
        /// at the head of every action the 7z session runs on its COM executor thread, so a test can
        /// assert every <see cref="IInArchive"/> touch happens on the one owning thread.
        /// </summary>
        internal static Action<int> OnSessionComWork;

        /// <summary>
        /// Serialises <b>and pins the thread of</b> every 7z.dll COM call for one reading session
        /// (design §15 #1). <see cref="IInArchive"/>, its <see cref="InStreamWrapper"/> and the
        /// <see cref="MultiExtractToStreamsCallback"/> are apartment-bound COM objects; invoking them
        /// from a thread other than their creator crosses an apartment boundary and can fault
        /// natively with no managed exception (the fast-flip <c>AccessViolation</c> class). The
        /// pipeline's caller-side lock is not sufficient - correctness needs thread affinity. One
        /// long-lived STA thread (plain dedicated thread off Windows - 7z.dll is Windows-only, but
        /// the affinity guarantee still holds) drains a work queue; the archive handle is created,
        /// used and released only inside actions posted here.
        /// </summary>
        private sealed class ComExecutor : IDisposable
        {
            private readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
            private readonly Thread thread;

            public ComExecutor()
            {
                thread = new Thread(Loop) { IsBackground = true, Name = "7z-session-com" };
                if (OperatingSystem.IsWindows())
                {
                    try { thread.SetApartmentState(ApartmentState.STA); }
                    catch (PlatformNotSupportedException) { }
                    catch (InvalidOperationException) { }
                }
                thread.Start();
            }

            private void Loop()
            {
                try
                {
                    foreach (Action work in queue.GetConsumingEnumerable())
                    {
                        try { work(); } catch { }
                    }
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }

            public T Run<T>(Func<T> work)
            {
                if (Thread.CurrentThread == thread)
                {
                    return work();
                }

                T result = default;
                Exception error = null;
                using ManualResetEventSlim done = new ManualResetEventSlim(false);
                try
                {
                    queue.Add(() =>
                    {
                        OnSessionComWork?.Invoke(Environment.CurrentManagedThreadId);
                        try { result = work(); }
                        catch (Exception ex) { error = ex; }
                        finally { done.Set(); }
                    });
                }
                catch (InvalidOperationException)
                {
                    // Queue already completed (Dispose raced) - last-resort inline run.
                    return work();
                }

                done.Wait();
                if (error != null)
                {
                    ExceptionDispatchInfo.Capture(error).Throw();
                }
                return result;
            }

            public void Run(Action work) => Run<object>(() => { work(); return null; });

            public void Dispose()
            {
                try { queue.CompleteAdding(); } catch { }
                try { if (thread.IsAlive) thread.Join(TimeSpan.FromSeconds(5)); } catch { }
                try { queue.Dispose(); } catch { }
            }
        }

        private sealed class SevenZipAccessorSession : IComicAccessorSession
        {
            private const int ForwardBatch = 8;

            private readonly ComExecutor executor;
            private readonly IDisposable handle;
            private readonly IInArchive archive;
            private readonly Dictionary<string, int> nameToIndex;
            private readonly Dictionary<int, byte[]> passedByBuffer = new Dictionary<int, byte[]>();
            private int maxExtracted = -1;

            private SevenZipAccessorSession(ComExecutor executor, IDisposable handle, IInArchive archive, Dictionary<string, int> nameToIndex)
            {
                this.executor = executor;
                this.handle = handle;
                this.archive = archive;
                this.nameToIndex = nameToIndex;
            }

            public static SevenZipAccessorSession TryOpen(SevenZipEngine owner, string source)
            {
                ComExecutor executor = new ComExecutor();
                SevenZipAccessorSession session = executor.Run(() =>
                {
                    IInArchive archive = null;
                    IDisposable handle = null;
                    try
                    {
                        handle = owner.OpenArchive(source, out archive);
                        int count = archive.GetNumberOfItems();
                        var map = new Dictionary<string, int>(count, StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < count; i++)
                        {
                            PropVariant path = default(PropVariant);
                            archive.GetProperty(i, ItemPropId.kpidPath, ref path);
                            string name = path.GetObject()?.ToString();
                            if (!string.IsNullOrEmpty(name))
                            {
                                map[name] = i;
                            }
                        }
                        return new SevenZipAccessorSession(executor, handle, archive, map);
                    }
                    catch (System.Exception ex)
                    {
                        LastSessionOpenError = ex;
                        handle?.Dispose();
                        return null;
                    }
                });

                if (session == null)
                {
                    executor.Dispose();
                }
                return session;
            }

            public int Count => nameToIndex.Count;

            public byte[] ReadEntryBytes(string entryName)
            {
                if (entryName == null || !nameToIndex.TryGetValue(entryName, out int index))
                {
                    return null;
                }

                // Everything below - the passedByBuffer lookup, the Extract, the forward mark -
                // runs on the one COM thread, so passedByBuffer/maxExtracted need no lock even when
                // ReadEntryBytes is called concurrently from several threads (they queue).
                return executor.Run(() => ExtractOnComThread(index));
            }

            private byte[] ExtractOnComThread(int index)
            {
                if (passedByBuffer.TryGetValue(index, out byte[] buffered))
                {
                    passedByBuffer.Remove(index);
                    return buffered;
                }

                try
                {
                    bool forward = index > maxExtracted && index <= maxExtracted + ForwardBatch;
                    int start = forward ? maxExtracted + 1 : index;
                    int[] indices = new int[index - start + 1];
                    var sizes = new Dictionary<int, long>(indices.Length);
                    for (int k = 0; k < indices.Length; k++)
                    {
                        indices[k] = start + k;
                        sizes[indices[k]] = EntrySize(indices[k]); // before Extract - 7z.dll forbids reentrant GetProperty
                    }

                    var callback = new MultiExtractToStreamsCallback(indices, sizes);
                    archive.Extract(indices, indices.Length, 0, callback);
                    var results = callback.GetResults();

                    if (index > maxExtracted)
                    {
                        maxExtracted = index;
                    }

                    byte[] wanted = null;
                    foreach (var kv in results)
                    {
                        if (kv.Key == index)
                        {
                            wanted = kv.Value;
                        }
                        else
                        {
                            passedByBuffer[kv.Key] = kv.Value;
                        }
                    }
                    return wanted;
                }
                catch
                {
                    return null;
                }
            }

            /// <summary>Uncompressed size of entry <paramref name="index"/> (kpidSize), or 0 if unavailable - lets <see cref="MultiExtractToStreamsCallback"/> pre-size each stream (design §4.7). Runs on the COM thread (only ever called from <see cref="ExtractOnComThread"/>).</summary>
            private long EntrySize(int index)
            {
                try
                {
                    PropVariant size = default(PropVariant);
                    archive.GetProperty(index, ItemPropId.kpidSize, ref size);
                    return size.longValue;
                }
                catch
                {
                    return 0;
                }
            }

            public void Dispose()
            {
                try
                {
                    executor.Run(() =>
                    {
                        passedByBuffer.Clear();
                        handle?.Dispose();
                    });
                }
                catch { }
                executor.Dispose();
            }
        }

        private IDisposable OpenArchive(string source, out IInArchive archive)
        {
            IInArchive a = (archive = SevenZipFactory.CreateInArchive(MapFileFormat(base.Format)));
            InStreamWrapper archiveStream = new InStreamWrapper(File.OpenRead(source));
            long maxCheckStartPosition = SevenZipCheckSize;
            if (archive.Open(archiveStream, ref maxCheckStartPosition, new StubOpenCallback()) != 0)
            {
                archiveStream.Dispose();
                throw new FileLoadException();
            }
            return new Disposer(delegate
            {
                archiveStream.Dispose();
                a.Close();
                Marshal.ReleaseComObject(a);
            });
        }

        private static byte[] GetFileData(IInArchive archive, int fileNumber)
        {
            MemoryStream memoryStream;
            try
            {
                PropVariant value = default(PropVariant);
                archive.GetProperty(fileNumber, ItemPropId.kpidSize, ref value);
                memoryStream = new MemoryStream((int)value.longValue);
            }
            catch (Exception)
            {
                memoryStream = new MemoryStream();
            }
            archive.Extract(new int[1]
            {
                fileNumber
            }, 1, 0, new ExtractToStreamCallback(fileNumber, memoryStream));
            return memoryStream.ToArray();
        }

        private byte[] GetFileData(string source, string file)
        {
            try
            {
                if (libraryMode)
                {
                    IInArchive archive;
                    using (OpenArchive(source, out archive))
                    {
                        int numberOfItems = archive.GetNumberOfItems();
                        for (int i = 0; i < numberOfItems; i++)
                        {
                            PropVariant value = default(PropVariant);
                            archive.GetProperty(i, ItemPropId.kpidPath, ref value);
                            if (file.Equals(value.GetObject().ToString(), StringComparison.OrdinalIgnoreCase))
                            {
                                return GetFileData(archive, i);
                            }
                        }
                    }
                }
                else
                {
                    ExecuteProcess.Result result = ExecuteProcess.Execute(PackExe, "e -so \"" + FileMethods.GetShortName(source) + "\" \"" + file + "\"", ExecuteProcess.Options.StoreOutput);
                    if (result.ExitCode == 0)
                    {
                        return result.Output;
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private byte[] GetFileData(string source, ProviderImageInfo ii)
        {
            if (!libraryMode)
            {
                return GetFileData(source, ii.Name);
            }
            try
            {
                IInArchive archive;
                using (OpenArchive(source, out archive))
                {
                    return GetFileData(archive, ii.Index);
                }
            }
            catch
            {
                return null;
            }
        }

        private T Read<T>(string source) where T : class
        {
            try
            {
                return XmlInfoProviders.Readers.DeserializeAll<T>(s => new MemoryStream(GetFileData(source, s))) as T;
            }
            catch
            {
                return null;
            }
        }

        public override T ReadInfo<T>(string source) => Read<T>(source);

        public override bool WriteInfo(string source, ComicInfo comicInfo)
        {
            return UpdateComicInfos(source, base.Format,  comicInfo);
        }

        private static KnownSevenZipFormat MapFileFormat(int format)
        {
            switch (format)
            {
                case KnownFileFormats.CBZ:
                    return KnownSevenZipFormat.Zip;
                case KnownFileFormats.CB7:
                    return KnownSevenZipFormat.SevenZip;
                case KnownFileFormats.CBT:
                    return KnownSevenZipFormat.Tar;
                case KnownFileFormats.CBR:
                    return KnownSevenZipFormat.Rar;
                case KnownFileFormats.RAR5:
                    return KnownSevenZipFormat.Rar5;
                default:
                    throw new NotSupportedException("Type if not supported");
            }
        }

        // InlineUpdate: pass the byte[] directly to 7-Zip to update the archive without needing to create a temporary file. Only works with some formats, so for the others we need to create a temporary file.
        record UpdateSettings(bool InlineUpdate, string arg);
        public static bool UpdateComicInfo(string file, int format, ComicInfo comicInfo)
        {
            UpdateSettings setting = format switch
            {
                KnownFileFormats.CBZ => new UpdateSettings(InlineUpdate: false, arg: "zip"),
                KnownFileFormats.CB7 => new UpdateSettings(InlineUpdate: true, arg: "7z"),
                KnownFileFormats.CBT => new UpdateSettings(InlineUpdate: false, arg: "tar"),
                _ => throw new NotSupportedException("Format not supported for updating ComicInfo.xml")
            };

            return Update(file, comicInfo, setting);
        }

        /// <summary>
        /// Will update both the ComicInfo.xml & ComicBook.xml at the same time if the provided <paramref name="comicInfo"/> is a <see cref="ComicBook"/>. Otherwise only the ComicInfo.xml will be updated.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public static bool UpdateComicInfos(string file, int format, ComicInfo comicInfo)
        {
            UpdateSettings setting = format switch
            {
                KnownFileFormats.CBZ => new UpdateSettings(InlineUpdate: false, arg: "zip"),
                KnownFileFormats.CB7 => new UpdateSettings(InlineUpdate: false, arg: "7z"),
                KnownFileFormats.CBT => new UpdateSettings(InlineUpdate: false, arg: "tar"),
                _ => throw new NotSupportedException("Format not supported for updating ComicInfo.xml")
            };

            List<ComicInfo> infos = new List<ComicInfo>();
            infos.Add(comicInfo.GetInfo());
            if (comicInfo is ComicBook cb)
                infos.Add(cb); // Do a Clone?

            return UpdateAll(file, infos, setting);
        }

        private static bool Update(string file, ComicInfo comicInfo, UpdateSettings updateSetting)
        {
            try
            {
                string filename = comicInfo is ComicBook ? "ComicBook.xml" : "ComicInfo.xml";
                if (updateSetting.InlineUpdate)
                {
                    string parameters = $"u -t{updateSetting.arg} -si{filename} \"{file}\"";
                    return ExecuteUpdateProcess(parameters, comicInfo.ToArray());
                }
                else
                {
                    string tempDir = Path.Combine(EngineConfiguration.Default.TempPath, Guid.NewGuid().ToString());
                    string tempPath = Path.Combine(tempDir, filename);
                    try
                    {
                        Directory.CreateDirectory(tempDir);
                        using (Stream outStream = File.Create(tempPath))
                        {
                            comicInfo.Serialize(outStream);
                        }
                        string parameters2 = $"u -t{updateSetting.arg} \"{file}\" \"{tempPath}\"";
                        return ExecuteUpdateProcess(parameters2);
                    }
                    finally
                    {
                        try
                        {
                            FileUtility.SafeDelete(tempPath);
                            Directory.Delete(tempDir);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch (WriteErrorException) // We only want the WriteErrorException to be propagated, so that it shows the error message to the user.
            {
                throw;
            }
            catch (Exception)
            {
            }
            return false;
        }

        private static bool UpdateAll(string file, IEnumerable<ComicInfo> infos, UpdateSettings updateSetting)
        {
            try
            {
                string tempDir = Path.Combine(EngineConfiguration.Default.TempPath, Guid.NewGuid().ToString());
                List<string> tempsPaths = new List<string>();
                try
                {
                    Directory.CreateDirectory(tempDir);
                    foreach (var ci in infos)
                    {
                        string filename = ci is ComicBook ? "ComicBook.xml" : "ComicInfo.xml";
                        string tempPath = Path.Combine(tempDir, filename);
                        tempsPaths.Add(tempPath);
                        using (Stream outStream = File.Create(tempPath))
                        {
                            ci.Serialize(outStream);
                        } 
                    }
                    string parameters = GetParameters(tempsPaths.ToArray(), updateSetting, file);
                    return ExecuteUpdateProcess(parameters);
                }
                finally
                {
                    try
                    {
                        tempsPaths.ForEach(s => FileUtility.SafeDelete(s));
                        Directory.Delete(tempDir);
                    }
                    catch
                    {
                    }
                }
            }
            catch (WriteErrorException) // We only want the WriteErrorException to be propagated, so that it shows the error message to the user.
            {
                throw;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string GetParameters(string[] tempPaths, UpdateSettings updateSetting, string file)
        {
            //string parameters2 = $"u -t{updateSetting.arg} \"{file}\" \"{tempPath}\"";
            StringBuilder sb = new StringBuilder();
            sb.Append($"u -t{updateSetting.arg} \"{file}\"");
            foreach (var tempPath in tempPaths)
            {
                sb.Append(" ");
                sb.Append($"\"{tempPath}\"");
            }
            return sb.ToString().Trim();
        }

        private static bool ExecuteUpdateProcess(string parameters, byte[] inputData = null)
        {
            ExecuteProcess.Result result = ExecuteProcess.Execute(PackExe, parameters, inputData, null, ExecuteProcess.Options.StoreOutput);
            if (result.ExitCode == 0)
            {
                return true;
            }
            else // ExitCode 1 is a non fatal Error, so it might still have updated the ComicInfo.xml, but we want to check the error message to be sure.
            {
                //TODO: 7-Zip leaves .tmp files that should be cleaned up in the directory.
                string t = result.ConsoleText;
                string errorMessage = rxError.IsMatch(t) ? rxError.Match(t).Groups[1]?.Value.Trim() : string.Empty;

                if (!string.IsNullOrEmpty(errorMessage))
                    throw new WriteErrorException(errorMessage);

                return false;
            }
        }
    }
}
