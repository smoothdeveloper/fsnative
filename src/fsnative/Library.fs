namespace fsnative
open System
open System.Runtime.InteropServices

// code is taking inspiration of 
// https://github.com/mellinoe/nativelibraryloader
// https://github.com/MV10/dotnet-curses/
// Implementation here may eventually fall back to rely on nativelibraryloader once it settles in netcore

module Internals =
    module Windows =
        module Kernel32 =
            [<DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)>]
            extern IntPtr LoadLibrary(string lpFileName)
            
            [<DllImport("kernel32")>]
            extern IntPtr GetProcAddress(IntPtr dllHandle, string procName)
            
            [<DllImport("kernel32")>]
            extern int FreeLibrary(IntPtr dllHandle)
            
    module Linux =
        module libdl =
            // originally just "libdl"
            // https://github.com/mellinoe/nativelibraryloader/issues/2
            let [<Literal>] LibName = "libdl.so.2";

            let RTLD_NOW = 0x002;

            [<DllImport(LibName)>]
            extern IntPtr dlopen(string fileName, int flags)

            [<DllImport(LibName)>]
            extern IntPtr dlsym(IntPtr handle, string name)

            [<DllImport(LibName)>]
            extern int dlclose(IntPtr handle)

            [<DllImport(LibName)>]
            extern string dlerror()

    module OSX =
        module libdl =
            let [<Literal>] LibName = "libdl";

            let RTLD_NOW = 0x002;

            [<DllImport(LibName)>]
            extern IntPtr dlopen(string fileName, int flags)

            [<DllImport(LibName)>]
            extern IntPtr dlsym(IntPtr handle, string name)

            [<DllImport(LibName)>]
            extern int dlclose(IntPtr handle)

            [<DllImport(LibName)>]
            extern string dlerror()

    open System.Runtime.InteropServices            
    open System

    let (</>) a b = System.IO.Path.Combine(a,b)

    let (|StartsWith|_|) (v: string) p = if v.StartsWith p then Some () else None
      
    let (|Linux|OSX|Windows|Unknown|) v =
        if RuntimeInformation.IsOSPlatform OSPlatform.Linux then Linux
        elif RuntimeInformation.IsOSPlatform OSPlatform.OSX then OSX
        elif RuntimeInformation.IsOSPlatform OSPlatform.Windows then Windows
        else Unknown v
        
    let userDirectory =
        match () with
        | Windows -> Environment.GetEnvironmentVariable "USERPROFILE"
        | _       -> Environment.GetEnvironmentVariable "HOME"

    let nugetPackagesRootDirectory = userDirectory </> ".nuget" </> "packages"

    let guessFallbackRID actualRuntimeIdentifier =
        match actualRuntimeIdentifier with
        | "osx.10.13-x64" -> "osx.10.12-x64" //?
        | StartsWith "osx" -> "osx-x64"
        | _ -> null

module LibraryLoader =
  open Internals
  type [<Struct>] LoadedLibrary         = private LoadedLibrary of IntPtr
  type [<Struct>] LoadedFunctionPointer = private LoadedFunctionPointer of IntPtr
  type ILibraryLoader =
    abstract LoadLibrary: name: string -> LoadedLibrary option
    abstract LoadFunctionPointer: library: LoadedLibrary -> string -> LoadedFunctionPointer option
    abstract FreeLibrary: library: LoadedLibrary -> Result<unit, int>

  let linuxLoader =
    { new ILibraryLoader with
        member x.LoadLibrary name                                 = let ptr = Linux.libdl.dlopen(name, Linux.libdl.RTLD_NOW) in if ptr = IntPtr.Zero then None else Some (LoadedLibrary ptr)
        member x.LoadFunctionPointer (LoadedLibrary library) name = let ptr = Linux.libdl.dlsym(library, name)               in if ptr = IntPtr.Zero then None else Some (LoadedFunctionPointer ptr)
        member x.FreeLibrary         (LoadedLibrary library)      = let res = Linux.libdl.dlclose library                    in if res = 0 then Ok () else Error res }

  let osxLoader =
    { new ILibraryLoader with
        member x.LoadLibrary name                                 = let ptr = OSX.libdl.dlopen(name, OSX.libdl.RTLD_NOW) in if ptr = IntPtr.Zero then None else Some (LoadedLibrary ptr)
        member x.LoadFunctionPointer (LoadedLibrary library) name = let ptr = OSX.libdl.dlsym(library, name)             in if ptr = IntPtr.Zero then None else Some (LoadedFunctionPointer ptr)
        member x.FreeLibrary         (LoadedLibrary library)      = let res = OSX.libdl.dlclose library                  in if res = 0 then Ok () else Error res }

  let windowsLoader =
    { new ILibraryLoader with
        member x.LoadLibrary name                                 = let ptr = Windows.Kernel32.LoadLibrary name              in if ptr = IntPtr.Zero then None else Some (LoadedLibrary ptr)
        member x.LoadFunctionPointer (LoadedLibrary library) name = let ptr = Windows.Kernel32.GetProcAddress(library, name) in if ptr = IntPtr.Zero then None else Some (LoadedFunctionPointer ptr) 
        member x.FreeLibrary         (LoadedLibrary library)      = let res = Windows.Kernel32.FreeLibrary library           in if res = 0 then Ok () else Error res }

  let loadFunction l n (loader: ILibraryLoader) : 'f option =
    loader.LoadFunctionPointer l n 
    #if NET45
    |> Option.map (fun (LoadedFunctionPointer ptr) -> Marshal.GetDelegateForFunctionPointer(ptr, typeof<'f>) :?> 'f)
    #else
    |> Option.map (fun (LoadedFunctionPointer ptr) -> Marshal.GetDelegateForFunctionPointer ptr)
    #endif

  let withRuntimeLoader doWithLoader =
    doWithLoader (
      match () with
      | Windows -> windowsLoader
      | OSX -> osxLoader
      | Linux -> linuxLoader
      | _ -> failwithf "ain't gonna load"
    ) 

  let tryLoadLibrary names paths (loader: ILibraryLoader) =
    seq { for p in paths do for n in names do yield p </> n }
    |> Seq.choose loader.LoadLibrary
    |> Seq.tryHead

    