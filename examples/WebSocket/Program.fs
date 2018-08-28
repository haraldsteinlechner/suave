
open Suave
open Suave.Http
open Suave.Operators
open Suave.Filters
open Suave.Successful
open Suave.Files
open Suave.RequestErrors
open Suave.Logging
open Suave.Utils

open System
open System.Net

open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket

let ws (webSocket : WebSocket) (context: HttpContext) =
  socket {
    // if `loop` is set to false, the server will stop receiving messages
    let mutable loop = true

    while loop do
      // the server will wait for a message to be received without blocking the thread
      let msg = webSocket.read() |> Async.RunSynchronously

      match msg with
        | Choice1Of2 msg -> 
      
          match msg with
          // the message has type (Opcode * byte [] * bool)
          //
          // Opcode type:
          //   type Opcode = Continuation | Text | Binary | Reserved | Close | Ping | Pong
          //
          // byte [] contains the actual message
          //
          // the last element is the FIN byte, explained later
          | (Text, data, true) ->
            // the message can be converted to a string
            let str = UTF8.toString data
            let response = sprintf "response to %s" str

            // the response needs to be converted to a ByteSegment
            let byteResponse =
              response
              |> System.Text.Encoding.ASCII.GetBytes
              |> ByteSegment

            // the `send` function sends a message back to the client
            webSocket.send Text byteResponse true |> Async.RunSynchronously

          | (Close, _, _) ->
            let emptyResponse = [||] |> ByteSegment
            do! webSocket.send Close emptyResponse true

            // after sending a Close message, stop the loop
            loop <- false

          | _ -> ()
        | _ ->
          printfn "error"
    }

/// An example of explictly fetching websocket errors and handling them in your codebase.
let wsWithErrorHandling (webSocket : WebSocket) (context: HttpContext) = 
   
   let exampleDisposableResource = { new IDisposable with member __.Dispose() = printfn "Resource needed by websocket connection disposed" }
   let websocketWorkflow = ws webSocket context
   
   async {
    let! successOrError = websocketWorkflow
    match successOrError with
    // Success case
    | Choice1Of2() -> ()
    // Error case
    | Choice2Of2(error) ->
        // Example error handling logic here
        printfn "Error: [%A]" error
        exampleDisposableResource.Dispose()
        
    return successOrError
   }

let app : WebPart = 
  choose [
    path "/websocket" >=> handShake ws
    path "/websocketWithSubprotocol" >=> handShakeWithSubprotocol (chooseSubprotocol "test") ws
    path "/websocketWithError" >=> handShake wsWithErrorHandling
    GET >=> choose [ path "/" >=> file "index.html"; browseHome ]
    NOT_FOUND "Found no handlers." ]

[<EntryPoint>]
let main _ =
  let mutable workers, ios = 0,0 
  System.Threading.ThreadPool.GetMinThreads(&workers,&ios)
  printfn "original min threads: %A, IO threads: %A" workers ios
  System.Threading.ThreadPool.GetMaxThreads(&workers,&ios)
  printfn "original max threads: %A, IO threads: %A" workers ios

  // simulating 1 logical core...
  System.Threading.ThreadPool.SetMinThreads(1,1) |> printfn "setting min threads worked: %A"
  System.Threading.ThreadPool.SetMaxThreads(32767,1000) |> printfn "setting max threads worked: %A"

  System.Threading.ThreadPool.GetMinThreads(&workers,&ios)
  printfn "set min threads: %A, IO threads: %A" workers ios
  System.Threading.ThreadPool.GetMaxThreads(&workers,&ios)
  printfn "set max threads: %A, IO threads: %A" workers ios
   
  let sw = System.Diagnostics.Stopwatch.StartNew()
  let observer =
    async {
      do! Async.SwitchToNewThread()
      while true do
        let mutable workers, ios = 0,0 
        System.Threading.ThreadPool.GetAvailableThreads(&workers,&ios)
        printfn "%03.2f available workers: %A, io's: %A" sw.Elapsed.TotalMilliseconds workers ios 
        System.Threading.Thread.Sleep 50
    } |> Async.Start

  startWebServer { defaultConfig with logger = Targets.create Verbose [||] } app
  0

//
// The FIN byte:
//
// A single message can be sent separated by fragments. The FIN byte indicates the final fragment. Fragments
//
// As an example, this is valid code, and will send only one message to the client:
//
// do! webSocket.send Text firstPart false
// do! webSocket.send Continuation secondPart false
// do! webSocket.send Continuation thirdPart true
//
// More information on the WebSocket protocol can be found at: https://tools.ietf.org/html/rfc6455#page-34
//