using System;
using System.IO;
using System.Diagnostics;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace AutoPatchETS
{
    public class Patcher
    {
        public static bool PatchMethodReturn(AssemblyDef asm, string classPath, string methodName, int numArguments = 0)
        {
            MethodDef methodDef = FindMethod(asm, classPath, methodName);
            if (methodDef != null && methodDef.Parameters.Count == numArguments)
            {
                methodDef.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Ret));
                return true;
            }
            return false;
        }

        public static MethodDef FindMethod(TypeDef type, string methodName)
        {
            if (type != null)
            {
                foreach (MethodDef methodDef in type.Methods)
                {
                    if (methodDef.Name == methodName)
                    {
                        return methodDef;
                    }
                }
            }
            return null;
        }

        public static MethodDef FindMethod(AssemblyDef asm, string classPath, string methodName)
        {
            return FindMethod(FindType(asm, classPath), methodName);
        }

        public static TypeDef FindType(AssemblyDef asm, string classPath)
        {
            foreach (ModuleDef moduleDef in asm.Modules)
            {
                foreach (TypeDef typeDef in moduleDef.Types)
                {
                    if (typeDef.FullName == classPath)
                    {
                        return typeDef;
                    }
                }
            }
            return null;
        }

        public static bool PatchAssembly(string path, ref string error, PatchStrategy patcher)
        {
            string backupPath = path + ".bak";
            try
            {
                if (!File.Exists(backupPath))
                {
                    File.Copy(path, backupPath);
                }
                else
                {
                    File.Copy(backupPath, path, true);
                }
            }
            catch (Exception ex)
            {
                error = ex.ToString();
                return false;
            }
            try
            {
                DateTime creationTime = File.GetCreationTime(path);
                ModuleDefMD moduleDefMD = ModuleDefMD.Load(path);
                if (!patcher(moduleDefMD.Assembly))
                {
                    return false;
                }
                moduleDefMD.Write(path);
                File.SetCreationTime(path, creationTime);
                return true;
            }
            catch (Exception ex2)
            {
                File.Copy(backupPath, path, true);
                error = ex2.ToString();
            }
            return false;
        }

        public delegate bool PatchStrategy(AssemblyDef asm);
    }

    internal class Program
    {
        private static string CheckOutput(string fileName, string cmd, bool trim = true)
        {
            Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    FileName = fileName,
                    Arguments = cmd
                }
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return trim ? output.Trim() : output;
        }

        private static string CheckSigned(string fullPath)
        {
            return CheckOutput("powershell", $"(Get-AuthenticodeSignature -FilePath \"{fullPath}\").Status", true) == "Valid" ? "Yes" : "No";
        }

        public static void LocalMode(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("[+] Usage: AutoPatchETS.exe <path to onlinetestwks_o.exe>");
                return;
            }
            string path = args[0];
            if (!File.Exists(path))
            {
                Console.WriteLine("[-] File doesn't exist");
                return;
            }
            string fileName = Path.GetFileName(path);
            string directoryName = Path.GetDirectoryName(path);
            if (!fileName.Equals("onlinetestwks_o.exe", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[-] The filename must be onlinetestwks_o.exe");
                return;
            }
            Console.WriteLine("[+] Executable: " + fileName);
            Console.WriteLine("[+] Folder: \"" + directoryName + "\"");
            Console.Write("[+] Check if this file is the original file from ETS: ... ");
            string signatureStatus = CheckSigned(path);
            Console.WriteLine(signatureStatus);
            if (!signatureStatus.Equals("Yes", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[-] This is not the original onlinetestwks.exe. Please check again");
                return;
            }
            Console.WriteLine("[+] Patching now ...");
            ModuleDefMD moduleDefMD = ModuleDefMD.Load(path);
            foreach (ModuleDef moduleDef in moduleDefMD.Assembly.Modules)
            {
                foreach (TypeDef typeDef in moduleDef.Types)
                {
                    if (typeDef.FullName.Equals("ibt2.wks.TCManager", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (MethodDef methodDef in typeDef.Methods)
                        {
                            if (methodDef.Name.Equals("lockdown") || methodDef.Name.Equals("unlock"))
                            {
                                methodDef.Body.Instructions[0] = OpCodes.Ret.ToInstruction();
                                methodDef.Body.KeepOldMaxStack = true;
                            }
                        }
                    }
                    if (typeDef.FullName.Equals("ibt2.wks.GPOMain", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (MethodDef methodDef in typeDef.Methods)
                        {
                            if (methodDef.Name.Equals("checkAppChecksum"))
                            {
                                var instrs = methodDef.Body.Instructions;

                                // Insert at the start
                                instrs.Insert(0, OpCodes.Ldarg_0.ToInstruction());
                                instrs.Insert(1, OpCodes.Ldnull.ToInstruction());
                                instrs.Insert(2, OpCodes.Stind_Ref.ToInstruction());
                                instrs.Insert(3, OpCodes.Ldc_I4_1.ToInstruction());
                                instrs.Insert(4, OpCodes.Ret.ToInstruction());

                                methodDef.Body.KeepOldMaxStack = true;
                            }
                        }
                    }
                }
            }
            string newPath = Path.Combine(directoryName, "onlinetestwks.exe");
            moduleDefMD.Write(newPath);
            File.SetCreationTime(newPath, File.GetCreationTime(path));
            File.SetLastWriteTime(newPath, File.GetLastWriteTime(path));
            Console.WriteLine("[+] New file written to \"" + newPath + "\"");
            Console.WriteLine("[+] Patch successfully!");
        }

        public static void Main(string[] args)
        {
            LocalMode(args);
        }
    }
}
