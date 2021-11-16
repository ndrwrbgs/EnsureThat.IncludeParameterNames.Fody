using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace EnsureThat.IncludeParameterNames.Fody
{
    using System.Collections.Generic;
    using global::Fody;
    using Mono.Cecil.Cil;
    using Mono.Cecil.Rocks;

    /// <summary>
    /// Mostly following <see href="https://github.com/Fody/Ionad/blob/master/Ionad.Fody/ModuleWeaver.cs"/>
    /// I do not fully understand why some things are done (like SimplifyMacros())
    /// </summary>
    public class ModuleWeaver :
        BaseModuleWeaver
    {
        #region ShouldCleanReference

        /// <summary>Called when the weaver is executed.</summary>
        public override void Execute()
        {
            foreach (var type in ModuleDefinition.GetTypes())
            {
                foreach (var method in type.MethodsWithBody())
                {
                    ReplaceCalls(method.Body);
                }
            }

            // Use WeavingException for known failures
            //throw new NotImplementedException("Do replacement");
            WriteInfo("Did nothing");
        }

        private void ReplaceCalls(MethodBody body)
        {
            body.SimplifyMacros(); // TODO: Does this modify the target assembly? Should we really be doing this for everything?

            // We are seeking specific operations
            // In this case, we want to replace
            // That<T>([NoEnumeration] T value, string name = null, OptsFn optsFn = null)
            // calls that have `null` for name with `nameof(value)`, if possible
            var calls = body.Instructions.Where(i => i.OpCode == OpCodes.Call);
            foreach (var call in calls)
            {
                var originalMethodReference = (MethodReference)call.Operand;
                var originalMethodDefinition = originalMethodReference.Resolve();

                if (originalMethodReference.FullName.Contains("That"))
                    ;
            }

            for (var index = 0; index < body.Instructions.Count; index++)
            {
                var instruction = body.Instructions[index];
                if (instruction.OpCode == OpCodes.Call) // Search for the method TODO: Enhance to be more generic
                {
                    var originalMethodReference = (MethodReference)instruction.Operand;
                    var originalMethodDefinition = originalMethodReference.Resolve();
                    if (originalMethodDefinition.DeclaringType.Module.Assembly.Name.Name.Equals("Ensure.That")
                        && originalMethodDefinition.DeclaringType.FullName.Equals("EnsureThat.Ensure")
                        && originalMethodReference.Name.Equals("That"))
                    {
                        // Tie input arguments to IL preparing them -- note this is messy as calls to other methods could be present
                        // TODO: Handle more complex call scenarios
                        var optsFnInstruction = body.Instructions[index - 1];
                        if (optsFnInstruction.OpCode.FlowControl != FlowControl.Next)
                        {
                            WriteWarning("We operate on IL and not an AST, and cannot yet interpret how your call stack operates, please simplify the invocation for us if you could!");
                            // TODO: Can't handle Ensure.That(blah, blah, CallMethod()) [since CallMethod might itself need arguments too]
                            continue;
                        }

                        var nameInstruction = body.Instructions[index - 2];
                        if (nameInstruction.OpCode != OpCodes.Ldnull)
                        {
                            // Something is already being prepared! All good
                            WriteDebug($"{body.Method.Name}'s {index} OpCode calls the method, but supplies a `name`. Nothing to do");
                            continue;
                        }

                        // nameInstruction is null
                        var valueInstruction = body.Instructions[index - 3];
                        if (valueInstruction.OpCode != OpCodes.Ldarg)
                        {
                            WriteWarning("Cannot understand anything but Ldarg. Make sure you're calling Ensure.That() with a parameter to the method.");
                            continue;
                        }

                        var target = valueInstruction.Operand as ParameterDefinition;
                        if (target == null)
                        {
                            WriteError($"Unsupported Operand of type {valueInstruction.Operand?.GetType()}. Expecting ParameterDefinition.");
                            continue;
                        }
                        var nameOfParameter = target.Name;

                        body.Instructions[index - 2 /* index of nameInstruction */] = Instruction.Create(
                            OpCodes.Ldstr,
                            nameOfParameter);
                    }
                    // TODO: Ensure.That() should be annotated with [InvokerParameterName]
                }
            }

            // Why?
            body.InitLocals = true;
            body.OptimizeMacros();
        }

        /// <summary>
        /// Return a list of assembly names for scanning.
        /// Used as a list for <see cref="P:Fody.BaseModuleWeaver.FindType" />.
        /// </summary>
        /// <remarks>
        /// "This method should return all possible assemblies that the weaver may require while resolving types"
        /// </remarks>
        public override IEnumerable<string> GetAssembliesForScanning()
        {
            yield break;
            // It'll also, probably, need Ensure.That?
            //yield return "Ensure.That";
            //yield return "netstandard";
            //yield return "mscorlib";
        }

        public override bool ShouldCleanReference => true;
        #endregion
    }
}

internal static class CecilExtensions
{
    public static void RemoveStaticReplacementAttribute(this ICustomAttributeProvider definition)
    {
        var customAttributes = definition.CustomAttributes;

        var attribute = customAttributes.FirstOrDefault(x => x.AttributeType.Name == "StaticReplacementAttribute");

        if (attribute != null)
        {
            customAttributes.Remove(attribute);
        }
    }

    public static CustomAttribute GetStaticReplacementAttribute(this ICustomAttributeProvider value)
    {
        return value.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "StaticReplacementAttribute");
    }

    public static IEnumerable<MethodDefinition> MethodsWithBody(this TypeDefinition type)
    {
        return type.Methods.Where(x => x.Body != null);
    }

    public static IEnumerable<PropertyDefinition> ConcreteProperties(this TypeDefinition type)
    {
        return type.Properties.Where(x => (x.GetMethod == null || !x.GetMethod.IsAbstract) && (x.SetMethod == null || !x.SetMethod.IsAbstract));
    }

    static MethodReference CloneMethodWithDeclaringType(MethodDefinition methodDef, TypeReference declaringTypeRef)
    {
        if (!declaringTypeRef.IsGenericInstance || methodDef == null)
        {
            return methodDef;
        }

        var methodRef = new MethodReference(methodDef.Name, methodDef.ReturnType, declaringTypeRef)
        {
            CallingConvention = methodDef.CallingConvention,
            HasThis = methodDef.HasThis,
            ExplicitThis = methodDef.ExplicitThis
        };

        foreach (var paramDef in methodDef.Parameters)
        {
            methodRef.Parameters.Add(new ParameterDefinition(paramDef.Name, paramDef.Attributes, paramDef.ParameterType));
        }

        foreach (var genParamDef in methodDef.GenericParameters)
        {
            methodRef.GenericParameters.Add(new GenericParameter(genParamDef.Name, methodRef));
        }

        return methodRef;
    }

    public static MethodReference ReferenceMethod(this TypeReference typeRef, Func<MethodDefinition, bool> methodSelector)
    {
        return CloneMethodWithDeclaringType(typeRef.Resolve().Methods.FirstOrDefault(methodSelector), typeRef);
    }

    public static MethodReference ReferenceMethod(this TypeReference typeRef, MethodDefinition method)
    {
        return ReferenceMethod(typeRef, m =>
            m.Name == method.Name && Matches(m, method)
        );
    }

    static bool Matches(IMethodSignature left, IMethodSignature right)
    {
        return ReturnMatches(left, right) &&
               left.Parameters.Count == right.Parameters.Count &&
               left.Parameters.Zip(right.Parameters, Matches).All(x => x);
    }

    static bool Matches(ParameterDefinition left, ParameterDefinition right)
    {
        if (left.ParameterType == right.ParameterType)
            return true;
        if (left.ParameterType.IsGenericParameter && right.ParameterType.IsGenericParameter)
            return true;

        return false;
    }

    static bool Matches(TypeReference left, TypeReference right)
    {
        if (left.FullName == right.FullName)
            return true;
        if (left.IsGenericParameter && right.IsGenericParameter)
            return true;

        return false;
    }

    static bool ReturnMatches(IMethodSignature left, IMethodSignature right)
    {
        if (left.ReturnType.FullName == right.ReturnType.FullName &&
            left.ReturnType.GenericParameters.Zip(right.ReturnType.GenericParameters, Matches).All(x => x)
        )
            return true;

        if (left.ReturnType.IsGenericParameter && right.ReturnType.IsGenericParameter)
        {
            return true;
        }

        return false;
    }
}