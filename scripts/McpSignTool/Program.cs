using System;
using System.IO;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

// Iter-32 MCP-007 — one-shot helper:
//   dotnet run -- gen-key   <out_dir>     produces release.pub + release.sec
//   dotnet run -- sign      <pub> <sec> <manifest_path>  writes <manifest>.sig
// Invoked once per repo to seed mcp-connectors/_signing/ + per-connector .sig
// files. Production rotation is documented in docs/04-security/security-architecture.md.

if (args.Length == 0) { Console.Error.WriteLine("usage: gen-key <dir> | sign <pubB64File> <secB64File> <manifest>"); return 1; }

if (args[0] == "gen-key")
{
    var dir = args[1];
    Directory.CreateDirectory(dir);
    var rng = new SecureRandom();
    var seed = new byte[32];
    rng.NextBytes(seed);
    var sk = new Ed25519PrivateKeyParameters(seed, 0);
    var pk = sk.GeneratePublicKey();
    File.WriteAllText(Path.Combine(dir, "release.pub"), Convert.ToBase64String(pk.GetEncoded()));
    File.WriteAllText(Path.Combine(dir, "release.sec"), Convert.ToBase64String(seed));
    Console.WriteLine("wrote release.pub + release.sec to " + Path.GetFullPath(dir));
    return 0;
}

if (args[0] == "sign")
{
    var pubB64 = File.ReadAllText(args[1]).Trim();
    var secB64 = File.ReadAllText(args[2]).Trim();
    var manifestPath = args[3];
    var seed = Convert.FromBase64String(secB64);
    var sk = new Ed25519PrivateKeyParameters(seed, 0);
    var bytes = File.ReadAllBytes(manifestPath);
    var signer = new Ed25519Signer();
    signer.Init(true, sk);
    signer.BlockUpdate(bytes, 0, bytes.Length);
    var sig = signer.GenerateSignature();
    var sigPath = manifestPath + ".sig";
    File.WriteAllText(sigPath, Convert.ToBase64String(sig));
    Console.WriteLine("signed " + manifestPath + " -> " + sigPath);
    // sanity verify
    var pk = new Ed25519PublicKeyParameters(Convert.FromBase64String(pubB64), 0);
    var v = new Ed25519Signer();
    v.Init(false, pk);
    v.BlockUpdate(bytes, 0, bytes.Length);
    if (!v.VerifySignature(sig)) { Console.Error.WriteLine("self-verify FAILED"); return 2; }
    return 0;
}

Console.Error.WriteLine("unknown command: " + args[0]);
return 1;
