using ClanIronSim.CommandLine;
using HawkNet.CommandLine;

return await CommandLineProgram.RunAsync(args, [new ClanIronCliExtension()], applicationName: "claniron");
