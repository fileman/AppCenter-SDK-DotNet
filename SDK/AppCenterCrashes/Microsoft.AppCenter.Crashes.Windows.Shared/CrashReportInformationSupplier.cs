using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AppCenter.Crashes.Ingestion.Models;
using ModelException = Microsoft.AppCenter.Crashes.Ingestion.Models.Exception;
using ModelStackFrame = Microsoft.AppCenter.Crashes.Ingestion.Models.StackFrame;
using ModelBinary = Microsoft.AppCenter.Crashes.Ingestion.Models.Binary;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.AppCenter.Crashes
{
    public class CrashReportInformationSupplier
    {
        public static void AddToErrorReport(ManagedErrorLog log, System.Exception exception)
        {
            log.Threads = new List<Thread> { new Thread(Environment.CurrentManagedThreadId, new List<ModelStackFrame>()) };
            log.Binaries = new List<ModelBinary>();
            AddExceptionInfo(log, null, exception, new HashSet<long>());
        }

        public static void AddExceptionInfo(ManagedErrorLog log, ModelException outerException, System.Exception exception, HashSet<long> seenBinaries)
        {
            var modelException = new ModelException
            {
                Type = exception.GetType().ToString(),
                Message = exception.Message,
                StackTrace = exception.StackTrace
            };
            if (exception is AggregateException aggregateException)
            {
                if (aggregateException.InnerExceptions.Count != 0)
                {
                    modelException.InnerExceptions = new List<ModelException>();
                    foreach (var innerException in aggregateException.InnerExceptions)
                    {
                        AddExceptionInfo(log, modelException, innerException, seenBinaries);
                    }
                }
            }
            if (exception.InnerException != null)
            {
                modelException.InnerExceptions = modelException.InnerExceptions ?? new List<ModelException>();
                AddExceptionInfo(log, modelException, exception.InnerException, seenBinaries);
            }
            var stackTrace = new StackTrace(exception, true);
            var frames = stackTrace.GetFrames();
            modelException.Frames = new List<ModelStackFrame>();

            // stackTrace.GetFrames may return null (happened on Outlook Groups application). 
            // HasNativeImage() method invoke on first frame is required to understand whether an application is compiled in native tool chain
            // and we can extract the frame addresses or not.
            if (frames != null && frames.Length > 0 && frames[0].HasNativeImage())
            {
                foreach (var frame in stackTrace.GetFrames())
                {
                    var crashFrame = new ModelStackFrame
                    {
                        Address = string.Format(CultureInfo.InvariantCulture, "0x{0:x16}", frame.GetNativeIP().ToInt64()),
                    };
                    log.Threads[0].Frames.Add(crashFrame);
                    modelException.Frames.Add(crashFrame);
                    long nativeImageBase = frame.GetNativeImageBase().ToInt64();
                    if (seenBinaries.Contains(nativeImageBase) || nativeImageBase == 0)
                    {
                        continue;
                    }
                    var binary = ImageToBinary(frame.GetNativeImageBase());
                    if (binary != null)
                    {
                        log.Binaries.Add(binary);
                        seenBinaries.Add(nativeImageBase);
                    }
                }
            }
            if (outerException == null)
            {
                log.Exception = modelException;
            }
            else
            {
                outerException.InnerExceptions.Add(modelException);
            }
        }

#if WINDOWS_UWP
        private static unsafe ModelBinary ImageToBinary(IntPtr imageBase)
        {
            var reader = new System.Reflection.PortableExecutable.PEReader((byte*)imageBase.ToPointer(), int.MaxValue, true);
            var debugdir = reader.ReadDebugDirectory();
            var codeViewEntry = debugdir.First(entry => entry.Type == System.Reflection.PortableExecutable.DebugDirectoryEntryType.CodeView);
            var codeView = reader.ReadCodeViewDebugDirectoryData(codeViewEntry);
            var pdbPath = Path.GetFileName(codeView.Path);
            var endAddress = imageBase + reader.PEHeaders.PEHeader.SizeOfImage;
            return new ModelBinary
            {
                StartAddress = string.Format(CultureInfo.InvariantCulture, "0x{0:x16}", imageBase.ToInt64()),
                EndAddress = string.Format(CultureInfo.InvariantCulture, "0x{0:x16}", endAddress.ToInt64()),
                Path = pdbPath,
                Name = string.IsNullOrEmpty(pdbPath) == false ? Path.GetFileNameWithoutExtension(pdbPath) : null,
                Id = string.Format(CultureInfo.InvariantCulture, "{0:N}-{1}", codeView.Guid, codeView.Age)
            };
        }
#else
        private static ModelBinary ImageToBinary(IntPtr imageBase)
        {
            return null;
        }
#endif
    }

    // TODO This can be removed if the code is moved to the UWP project.
#if NET45
    // some place holders while I figure out if the PEReader/Native image debug info is accessible in a similar manner as the UWP implementaiton
    // TODO - repo case
    static class StackFrameExtensions
    {
        public static bool HasNativeImage(this System.Diagnostics.StackFrame stackFrame)
        {
            return stackFrame.GetNativeImageBase() != IntPtr.Zero;
        }

        public static IntPtr GetNativeImageBase(this System.Diagnostics.StackFrame stackFrame)
        {
            return Marshal.GetHINSTANCE(stackFrame.GetMethod().Module.Assembly.ManifestModule);
        }

        public static IntPtr GetNativeIP(this System.Diagnostics.StackFrame stackFrame)
        {
            // Definitely wrong, but we need to return something
            // probably need something like this https://msdn.microsoft.com/en-us/library/dn832657(v=vs.110).aspx (only works on .net 4.6)
            return IntPtr.Add(GetNativeImageBase(stackFrame), stackFrame.GetNativeOffset());
        }
    }
#endif
}
