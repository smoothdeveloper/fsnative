#r "../build/Debug/AnyCPU/netstandard2.0/fsnative.dll"
open System
let libNames = [|"portmidi_x64.dll";"portmidi_x86.dll";"libportmidi.dylib";"libportmidi.so"|]
let libPaths = [|@"C:\dev\src\gitlab.com\gauthier\portmidisharp\lib\win";"/usr/local/lib";|]

open fsnative

type [<Struct>] PmDeviceInfo =
  val mutable StructVersion : int
  val mutable Interface     : IntPtr
  val mutable Name          : IntPtr
  val mutable Input         : int
  val mutable Output        : int
  val mutable Opened        : int

let loader = LibraryLoader.withRuntimeLoader id
let library = LibraryLoader.tryLoadLibrary libNames libPaths loader
type CResult = delegate of unit -> int
type GetDeviceInfo = delegate of int -> PmDeviceInfo
match library with
| None -> printfn "couldn't load"
| Some library -> 
  let pmInit : CResult  = LibraryLoader.loadFunction library "Pm_Initialize" loader |> Option.get
  let pmTerminate : CResult = LibraryLoader.loadFunction library "Pm_Terminate" loader |> Option.get
  let pmGetDeviceInfo: GetDeviceInfo = LibraryLoader.loadFunction library "Pm_GetDeviceInfo" loader |> Option.get
  let init = pmInit.Invoke()
  let d = pmGetDeviceInfo.Invoke 1 // not yet working
  let t = pmTerminate.Invoke()
  printfn "pmInit: %i %A %i" init d.Name t
