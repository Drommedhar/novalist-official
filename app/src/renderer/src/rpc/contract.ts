/**
 * Typed JSON-RPC method surface. This file is the single source of truth for
 * renderer-side types; Novalist.Backend.Tests serializes the C# facades and a
 * Vitest test compares the two so contract drift fails the build (M1+).
 */

export interface PingResult {
  pong: boolean
  version: string
}

export interface RpcContract {
  'system/ping': { params: undefined; result: PingResult }
  'system/shutdown': { params: undefined; result: void }
}
