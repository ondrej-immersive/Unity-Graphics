using UnityEngine;
using UnityEngine.Experimental.VFX;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.Experimental;

namespace UnityEditor.Experimental
{
    public static class CommonAttrib
    {
        public static VFXAttribute Seed =               new VFXAttribute("seed", VFXValueType.kUint);
        public static VFXAttribute Position =           new VFXAttribute("position", VFXValueType.kFloat3);
        public static VFXAttribute Velocity =           new VFXAttribute("velocity", VFXValueType.kFloat3);
        public static VFXAttribute Color =              new VFXAttribute("color", VFXValueType.kFloat3);
        public static VFXAttribute Alpha =              new VFXAttribute("alpha", VFXValueType.kFloat);
        public static VFXAttribute Phase =              new VFXAttribute("phase", VFXValueType.kFloat);
        public static VFXAttribute Size =               new VFXAttribute("size", VFXValueType.kFloat2);
        public static VFXAttribute Lifetime =           new VFXAttribute("lifetime", VFXValueType.kFloat);
        public static VFXAttribute Age =                new VFXAttribute("age", VFXValueType.kFloat);
        public static VFXAttribute Angle =              new VFXAttribute("angle", VFXValueType.kFloat);
        public static VFXAttribute AngularVelocity =    new VFXAttribute("angularVelocity", VFXValueType.kFloat);
        public static VFXAttribute TexIndex =           new VFXAttribute("texIndex", VFXValueType.kFloat);
    }

    public class VFXSystemRuntimeData
    {
        public Dictionary<VFXValue, string> uniforms = new Dictionary<VFXValue, string>();
        public Dictionary<VFXValue, string> outputUniforms = new Dictionary<VFXValue, string>();
        
        ComputeShader simulationShader; 
        public ComputeShader SimulationShader { get { return simulationShader; } }

        public Material m_Material = null;

        public VFXGeneratedTextureData m_GeneratedTextureData = null;

        int initKernel = -1;
        public int InitKernel { get { return initKernel; } }
        int updateKernel = -1;
        public int UpdateKernel { get { return updateKernel; } }

        public uint outputType; // tmp value to pass to C++
        public bool hasKill;

        public VFXBufferDesc[] buffersDesc;

        private List<ComputeBuffer> buffers = new List<ComputeBuffer>();

        public VFXSystemRuntimeData(ComputeShader shader)
        {
            simulationShader = shader;

            // FindKernel throws instead of setting value to -1
            try { initKernel = simulationShader.FindKernel("CSVFXInit"); }
            catch(Exception e) { initKernel = -1; }
            try { updateKernel = simulationShader.FindKernel("CSVFXUpdate"); }
            catch(Exception e) { updateKernel = -1; }
        }

        public void UpdateAllUniforms()
        {
            foreach (var uniform in uniforms)
                UpdateUniform(uniform.Key,false);

            foreach (var uniform in outputUniforms)
                UpdateUniform(uniform.Key, true);

            // Set generated texture data
            // atm set texture for both compute shaders, but can be improved by having info by kernel
            if (m_GeneratedTextureData.HasColorTexture())
            {
                if (initKernel != -1)
                    simulationShader.SetTexture(initKernel, "gradientTexture", m_GeneratedTextureData.ColorTexture);
                if (updateKernel != -1)
                    simulationShader.SetTexture(updateKernel, "gradientTexture", m_GeneratedTextureData.ColorTexture);
            }

            if (m_GeneratedTextureData.HasFloatTexture())
            {
                if (initKernel != -1)
                    simulationShader.SetTexture(initKernel, "curveTexture", m_GeneratedTextureData.FloatTexture);
                if (updateKernel != -1)
                    simulationShader.SetTexture(updateKernel, "curveTexture", m_GeneratedTextureData.FloatTexture);
            }
        }

        public void UpdateUniform(VFXValue value,bool output)
        {
            var currentUniforms = uniforms;
            if (output)
                currentUniforms = outputUniforms;

            string uniformName = currentUniforms[value];
            switch (value.ValueType)
            {
                case VFXValueType.kFloat:
                    if (output)
                        m_Material.SetFloat(uniformName, value.Get<float>());
                    else
                        simulationShader.SetFloat(uniformName,value.Get<float>());
                    break;
                case VFXValueType.kFloat2:
                {
                    Vector2 v = value.Get<Vector2>();
                    if (output)
                        m_Material.SetVector(uniformName, v);
                    else
                    {
                        float[] buffer = new float[2];                        
                        buffer[0] = v.x;
                        buffer[1] = v.y;
                        simulationShader.SetFloats(uniformName,buffer);  
                    }
                    break;
                }
                case VFXValueType.kFloat3:
                {     
                    Vector3 v = value.Get<Vector3>();
                    if (output)
                        m_Material.SetVector(uniformName, v);
                    else
                    {
                        float[] buffer = new float[3];
                        buffer[0] = v.x;
                        buffer[1] = v.y;
                        buffer[2] = v.z;
                        simulationShader.SetFloats(uniformName, buffer);
                    }
                    break;
                }
                case VFXValueType.kFloat4:
                    if (output)
                        m_Material.SetVector(uniformName, value.Get<Vector4>());
                    else
                        simulationShader.SetVector(uniformName,value.Get<Vector4>());
                    break;
                case VFXValueType.kInt:
                    if (output)
                        m_Material.SetInt(uniformName, value.Get<int>());
                    else
                        simulationShader.SetInt(uniformName,value.Get<int>());
                    break;
                case VFXValueType.kUint:
                    if (output)
                        m_Material.SetInt(uniformName, (int)value.Get<uint>());
                    else
                        simulationShader.SetInt(uniformName,(int)value.Get<uint>());
                    break;

                case VFXValueType.kTexture2D:
                case VFXValueType.kTexture3D:
                {
                    Texture tex = null;
                    if (value.ValueType == VFXValueType.kTexture2D)
                        tex = value.Get<Texture2D>();
                    else
                        tex = value.Get<Texture3D>();

                    if (tex != null)
                    {
                        if (output)
                            m_Material.SetTexture(uniformName, tex);
                        else
                        {
                            bool inInit = uniformName.Contains("init");
                            bool inUpdate = uniformName.Contains("update");
                            if (uniformName.Contains("global"))
                                inInit = inUpdate = true;

                            if (inInit && initKernel != -1)
                                simulationShader.SetTexture(initKernel, uniformName, tex);
                            if (inUpdate && updateKernel != -1)
                                simulationShader.SetTexture(updateKernel, uniformName, tex);
                        }
                    }

                    break;
                }
                case VFXValueType.kTransform:
                {
                    Matrix4x4 mat = value.Get<Matrix4x4>();
                    if (output)
                        m_Material.SetMatrix(uniformName, mat);
                    else
                    {
                        float[] buffer = new float[16];
                        for (int i = 0; i < 16; ++i)
                            buffer[i] = mat[i];
                        simulationShader.SetFloats(uniformName, buffer);
                    }
                    break;
                }

                case VFXValueType.kColorGradient:
                    if (output)
                        throw new NotImplementedException("TODO");
                    simulationShader.SetFloat(uniformName, m_GeneratedTextureData.GetGradientUniform(value));
                    break;

                case VFXValueType.kCurve:
                    if (output)
                        throw new NotImplementedException("TODO");
                    simulationShader.SetVector(uniformName, m_GeneratedTextureData.GetCurveUniform(value));
                    break;
                
                case VFXValueType.kNone:
                    // Not yet implemented
                    break;
            }
        }
    }

    public class AttributeBuffer
    {
        public AttributeBuffer(int index, int usage)
        {
            m_Index = index;
            m_Usage = usage;
            m_Attribs = new List<VFXAttribute>();
        }

        public void Add(VFXAttribute attrib)
        {
            m_Attribs.Add(attrib);
        }

        public void Add(AttributeBuffer other)
        {
            for (int i = 0; i < other.Count; ++i)
                m_Attribs.Add(other[i]);
            m_Usage |= other.m_Usage;
        }

        public int Index
        {
            get { return m_Index; }
        }

        // return usage per pass + RW
        public int Usage
        {
            get { return m_Usage; }
        }

        // return usage per pass
        public int MergedUsage
        {
            get { return ((m_Usage & 0xAA >> 1) | m_Usage) & 0x55; }
        }

        public int Count
        {
            get { return m_Attribs.Count; }
        }

        public VFXAttribute this[int index]
        {
            get { return m_Attribs[index]; }
        }

        public bool Used(VFXContextDesc.Type type)
        {
            return (m_Usage & (0x3 << (((int)type - 1) * 2))) != 0;
        }

        public bool Writable(VFXContextDesc.Type type)
        {
            return (m_Usage & (0x2 << (((int)type - 1) * 2))) != 0;
        }

        public int GetSizeInBytes()
        {
            int size = 0;
            foreach (VFXAttribute attrib in m_Attribs)
                size += VFXValue.TypeToSize(attrib.m_Type) * 4;
            return size;
        }

        int m_Index;
        int m_Usage;
        List<VFXAttribute> m_Attribs;
    }

    public class AttribComparer : IEqualityComparer<VFXAttribute>
    {
        public bool Equals(VFXAttribute attr0, VFXAttribute attr1)
        {
            return attr0.m_Name == attr1.m_Name && attr0.m_Type == attr1.m_Type;
        }

        public int GetHashCode(VFXAttribute attr)
        {
            return 13 * attr.m_Name.GetHashCode() + attr.m_Type.GetHashCode(); // Simple factored sum
        }
    }

    public class ShaderMetaData
    {
        public List<VFXBlockModel> initBlocks = new List<VFXBlockModel>();
        public List<VFXBlockModel> updateBlocks = new List<VFXBlockModel>();

        public bool hasKill;
        public bool hasRand;

        public List<AttributeBuffer> attributeBuffers = new List<AttributeBuffer>();
        public Dictionary<VFXAttribute, AttributeBuffer> attribToBuffer = new Dictionary<VFXAttribute, AttributeBuffer>(new AttribComparer());

        public HashSet<VFXValue> globalUniforms = new HashSet<VFXValue>();
        public HashSet<VFXValue> initUniforms = new HashSet<VFXValue>();
        public HashSet<VFXValue> updateUniforms = new HashSet<VFXValue>();

        public HashSet<VFXValue> globalSamplers = new HashSet<VFXValue>();
        public HashSet<VFXValue> initSamplers = new HashSet<VFXValue>();
        public HashSet<VFXValue> updateSamplers = new HashSet<VFXValue>();

        public HashSet<VFXValue> outputUniforms = new HashSet<VFXValue>();
        public HashSet<VFXValue> outputSamplers = new HashSet<VFXValue>();

        public Dictionary<VFXValue, string> paramToName = new Dictionary<VFXValue, string>();
        public Dictionary<VFXValue, string> outputParamToName = new Dictionary<VFXValue, string>();

        public VFXGeneratedTextureData generatedTextureData = new VFXGeneratedTextureData();
    }

    public static class VFXModelCompiler
    {
        public static VFXSystemRuntimeData CompileSystem(VFXSystemModel system)
        {
            // Create output compiler
            VFXOutputShaderGeneratorModule outputGenerator = null;
            VFXShaderGeneratorModule initGenerator = null;
            VFXShaderGeneratorModule updateGenerator = null;

            for (int i = 0; i < system.GetNbChildren(); ++i)
            {
                var model = system.GetChild(i);
                var desc = model.Desc;
                switch (desc.m_Type)
                {
                    case VFXContextDesc.Type.kTypeInit: initGenerator = desc.CreateShaderGenerator(model); break;
                    case VFXContextDesc.Type.kTypeUpdate: updateGenerator = desc.CreateShaderGenerator(model); break;
                    case VFXContextDesc.Type.kTypeOutput: outputGenerator = desc.CreateShaderGenerator(model) as VFXOutputShaderGeneratorModule; break;
                }
            }

            if (outputGenerator == null || initGenerator == null || updateGenerator == null) // Tmp: we need the 3 contexts atm
                return null;

            // BLOCKS
            List<VFXBlockModel> initBlocks = new List<VFXBlockModel>();
            List<VFXBlockModel> updateBlocks = new List<VFXBlockModel>();
            bool initHasRand = false;
            bool updateHasRand = false;
            bool updateHasKill = false;

            // Collapses the contexts into one big init and update
            for (int i = 0; i < system.GetNbChildren(); ++i)
            {
                VFXContextModel context = system.GetChild(i);

                List<VFXBlockModel> currentList = null; ;
                switch (context.GetContextType())
                {
                    case VFXContextDesc.Type.kTypeInit: currentList = initBlocks; break;
                    case VFXContextDesc.Type.kTypeUpdate: currentList = updateBlocks; break;
                }

                if (currentList == null)
                    continue;

                bool hasRand = false;
                bool hasKill = false;
                for (int j = 0; j < context.GetNbChildren(); ++j)
                {
                    VFXBlockModel blockModel = context.GetChild(j);
                    hasRand |= (blockModel.Desc.Flags & VFXBlockDesc.Flag.kHasRand) != 0;
                    hasKill |= (blockModel.Desc.Flags & VFXBlockDesc.Flag.kHasKill) != 0;
                    currentList.Add(blockModel);
                }

                switch (context.GetContextType())
                {
                    case VFXContextDesc.Type.kTypeInit: initHasRand |= hasRand; break;
                    case VFXContextDesc.Type.kTypeUpdate: 
                        updateHasRand |= hasRand;
                        updateHasKill |= hasKill;
                        break;
                }
            }

            if (initBlocks.Count == 0 && updateBlocks.Count == 0)
            {
                // Invalid system, not compiled
                VFXEditor.Log("System is invalid: Empty");
                return null;
            }

            // ATTRIBUTES (TODO Refactor the code !)
            Dictionary<VFXAttribute, int> attribs = new Dictionary<VFXAttribute, int>(new AttribComparer());

            CollectAttributes(attribs, initBlocks, 0);
            CollectAttributes(attribs, updateBlocks, 1);

            // Update flags with generators
            VFXBlockDesc.Flag initGeneratorFlags = VFXBlockDesc.Flag.kNone;
            if (!initGenerator.UpdateAttributes(attribs, ref initGeneratorFlags))
                return null;

            initHasRand |= (initGeneratorFlags & VFXBlockDesc.Flag.kHasRand) != 0;

            VFXBlockDesc.Flag updateGeneratorFlags = VFXBlockDesc.Flag.kNone;
            if (!updateGenerator.UpdateAttributes(attribs, ref updateGeneratorFlags))
                return null;

            updateHasRand |= (updateGeneratorFlags & VFXBlockDesc.Flag.kHasRand) != 0;
            updateHasKill |= (updateGeneratorFlags & VFXBlockDesc.Flag.kHasKill) != 0;

            VFXBlockDesc.Flag dummy = VFXBlockDesc.Flag.kNone;
            if (!outputGenerator.UpdateAttributes(attribs, ref dummy))
                return null;

            if (VFXEditor.AssetModel.PhaseShift)
            {
                if (attribs.ContainsKey(CommonAttrib.Position) && attribs.ContainsKey(CommonAttrib.Velocity))
                {
                    attribs[CommonAttrib.Phase] = 0x7; // Add phase attribute   
                    attribs[CommonAttrib.Position] = attribs[CommonAttrib.Position] | 0xF; // Ensure position is writable in init and update
                    attribs[CommonAttrib.Velocity] = attribs[CommonAttrib.Velocity] | 0x7; // Ensure velocity is readable in init and update

                    initHasRand = true; // phase needs rand as initialization
                }
                else
                {
                    VFXEditor.AssetModel.PhaseShift = false;
                    return null;
                }
            }

            // Add the seed attribute in case we need PRG
            if (initHasRand || updateHasRand)
            {
                updateHasRand = true;
                attribs[CommonAttrib.Seed] = (initHasRand ? 0x3 : 0x0) | (updateHasRand ? 0xC : 0x0);
            }

            // Find unitialized attribs and remove 
            List<VFXAttribute> unitializedAttribs = new List<VFXAttribute>(); 
            foreach (var attrib in attribs)
            {
                if ((attrib.Value & 0x3) == 0) // Unitialized attribute
                {
                    if (attrib.Key.m_Name != "seed" || attrib.Key.m_Name != "age") // Dont log anything for those as initialization is implicit
                        VFXEditor.Log("WARNING: " + attrib.Key.m_Name + " is not initialized. Use default value");
                    unitializedAttribs.Add(attrib.Key);
                }
                // TODO attrib to remove (when written and never used for instance) ! But must also remove blocks using them...
            }

            // Update the usage
            foreach (var attrib in unitializedAttribs)
                attribs[attrib] = attribs[attrib] | 0x3;

            // Sort attrib by usage and by size
            List<AttributeBuffer> buffers = VFXAttributePacker.Pack(attribs,6);

            if (buffers.Count > 6)
            {
                // TODO : Merge appropriate buffers in that case
                VFXEditor.Log("ERROR: too many buffers used (max is 6 + 2 reserved)");
                return null;
            }

            // Associate attrib to buffer
            var attribToBuffer = new Dictionary<VFXAttribute, AttributeBuffer>(new AttribComparer());
            foreach (var buffer in buffers)
                for (int i = 0; i < buffer.Count; ++i)
                    attribToBuffer.Add(buffer[i], buffer);

            VFXEditor.Log("Nb Attributes : " + attribs.Count);
            VFXEditor.Log("Nb Attribute buffers: " + buffers.Count);
            for (int i = 0; i < buffers.Count; ++i)
            {
                string str = "\t " + i + " |";
                for (int j = 0; j < buffers[i].Count; ++j)
                {
                    str += buffers[i][j].m_Name + "|";
                }
                str += " " + buffers[i].GetSizeInBytes() + "bytes";
                VFXEditor.Log(str);
            }
                
            // UNIFORMS
            HashSet<VFXValue> initUniforms = CollectUniforms(initBlocks);
            initGenerator.UpdateUniforms(initUniforms);
            HashSet<VFXValue> updateUniforms = CollectUniforms(updateBlocks);
            updateGenerator.UpdateUniforms(updateUniforms);

            // collect samplers
            HashSet<VFXValue> initSamplers = CollectAndRemoveSamplers(initUniforms);
            HashSet<VFXValue> updateSamplers = CollectAndRemoveSamplers(updateUniforms);

            // collect signals
            HashSet<VFXValue> initSignals = CollectAndRemoveSignals(initUniforms);
            HashSet<VFXValue> updateSignals = CollectAndRemoveSignals(updateUniforms);
            system.GeneratedTextureData.RemoveAllValues();
            system.GeneratedTextureData.AddValues(initSignals);
            system.GeneratedTextureData.AddValues(updateSignals);
            system.GeneratedTextureData.Generate();

            // Collect the intersection between init and update uniforms / samplers
            HashSet<VFXValue> globalUniforms = CollectIntersection(initUniforms, updateUniforms);
            HashSet<VFXValue> globalSamplers = CollectIntersection(initSamplers, updateSamplers);

            // Output stuff
            HashSet<VFXValue> outputUniforms = new HashSet<VFXValue>();
            outputGenerator.UpdateUniforms(outputUniforms);
            HashSet<VFXValue> outputSamplers = CollectAndRemoveSamplers(outputUniforms);

            // Associate VFXValue to generated name
            var paramToName = new Dictionary<VFXValue, string>();
            GenerateParamNames(paramToName, globalUniforms, "globalUniform");
            GenerateParamNames(paramToName, initUniforms, "initUniform");
            GenerateParamNames(paramToName, updateUniforms, "updateUniform");

            GenerateParamNames(paramToName, globalSamplers, "globalSampler");
            GenerateParamNames(paramToName, initSamplers, "initSampler");
            GenerateParamNames(paramToName, updateSamplers, "updateSampler");

            var outputParamToName = new Dictionary<VFXValue, string>();
            GenerateParamNames(outputParamToName, outputUniforms, "outputUniform");
            GenerateParamNames(outputParamToName, outputSamplers, "outputSampler");

            // Log result
            VFXEditor.Log("Nb init blocks: " + initBlocks.Count);
            VFXEditor.Log("Nb update blocks: " + updateBlocks.Count);
            VFXEditor.Log("Nb global uniforms: " + globalUniforms.Count);
            VFXEditor.Log("Nb init uniforms: " + initUniforms.Count);
            VFXEditor.Log("Nb update uniforms: " + updateUniforms.Count);

            ShaderMetaData shaderMetaData = new ShaderMetaData();
            shaderMetaData.initBlocks = initBlocks;
            shaderMetaData.updateBlocks = updateBlocks;
            shaderMetaData.hasRand = initHasRand || updateHasRand;
            shaderMetaData.hasKill = updateHasKill;
            shaderMetaData.attributeBuffers = buffers;
            shaderMetaData.attribToBuffer = attribToBuffer;
            shaderMetaData.globalUniforms = globalUniforms;
            shaderMetaData.initUniforms = initUniforms;
            shaderMetaData.updateUniforms = updateUniforms;
            shaderMetaData.globalSamplers = globalSamplers;
            shaderMetaData.initSamplers = initSamplers;
            shaderMetaData.updateSamplers = updateSamplers;
            shaderMetaData.outputUniforms = outputUniforms;
            shaderMetaData.outputSamplers = outputSamplers;
            shaderMetaData.paramToName = paramToName;
            shaderMetaData.outputParamToName = outputParamToName;
            shaderMetaData.generatedTextureData = system.GeneratedTextureData;
   
            string shaderSource = WriteComputeShader(shaderMetaData,initGenerator,updateGenerator);
            string outputShaderSource = WriteOutputShader(system,shaderMetaData,outputGenerator);

            string shaderName = "VFX_";
            shaderName += system.Id;

            VFXEditor.Log("\n**** SHADER CODE ****");
            VFXEditor.Log(shaderSource);
            VFXEditor.Log(outputShaderSource);
            VFXEditor.Log("\n*********************");

            // Write to file
            string shaderPath = Application.dataPath + "/VFXEditor/Generated/";
            System.IO.Directory.CreateDirectory(shaderPath);
            System.IO.File.WriteAllText(shaderPath + shaderName + ".compute", shaderSource);
            System.IO.File.WriteAllText(shaderPath + shaderName + ".shader", outputShaderSource); 

            string simulationShaderPath = "Assets/VFXEditor/Generated/" + shaderName + ".compute";
            string outputShaderPath = "Assets/VFXEditor/Generated/" + shaderName + ".shader";

            AssetDatabase.ImportAsset(simulationShaderPath);
            AssetDatabase.ImportAsset(outputShaderPath);

            VFXEditor.AssetModel.Invalidate(VFXElementModel.InvalidationCause.kParamChanged); // TMP Trigger a uniform reload as importing asset cause material properties to be invalidated

            ComputeShader simulationShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(simulationShaderPath);
            Shader outputShader = AssetDatabase.LoadAssetAtPath<Shader>(outputShaderPath);

            VFXSystemRuntimeData rtData = new VFXSystemRuntimeData(simulationShader);

            rtData.m_Material = new Material(outputShader);
            rtData.outputType = outputGenerator.GetSingleIndexBuffer(shaderMetaData) != null ? 1u : 0u; // This is temp
            rtData.hasKill = shaderMetaData.hasKill;

            rtData.m_GeneratedTextureData = system.GeneratedTextureData;

            // Build the buffer desc to send to component
            var buffersDesc = new List<VFXBufferDesc>();
            foreach (var attribBuffer in shaderMetaData.attributeBuffers)
            {
                VFXBufferDesc bufferDesc = new VFXBufferDesc();

                int structSize = attribBuffer.GetSizeInBytes();
                if (structSize == 12)
                    structSize = 16;
                bufferDesc.size = (uint)structSize;

                string bufferName = "attribBuffer" + attribBuffer.Index;
                if (attribBuffer.Used(VFXContextDesc.Type.kTypeInit))
                    bufferDesc.initName = bufferName + (attribBuffer.Writable(VFXContextDesc.Type.kTypeInit) ? "" : "_RO");
                if (attribBuffer.Used(VFXContextDesc.Type.kTypeUpdate))
                    bufferDesc.updateName = bufferName + (attribBuffer.Writable(VFXContextDesc.Type.kTypeUpdate) ? "" : "_RO");
                if (attribBuffer.Used(VFXContextDesc.Type.kTypeOutput))
                    bufferDesc.outputName = bufferName;

                buffersDesc.Add(bufferDesc);
            }

            rtData.buffersDesc = buffersDesc.ToArray();

            // Add uniforms mapping
            rtData.uniforms = shaderMetaData.paramToName;
            rtData.outputUniforms = shaderMetaData.outputParamToName;

            // Finally set uniforms
            rtData.UpdateAllUniforms();

            return rtData;
        }

        public static HashSet<VFXValue> CollectUniforms(List<VFXBlockModel> blocks)
        {
            HashSet<VFXValue> uniforms = new HashSet<VFXValue>();

            List<VFXNamedValue> collectedValues = new List<VFXNamedValue>();
            foreach (VFXBlockModel block in blocks)
                for (int i = 0; i < block.Desc.Properties.Length; ++i)
                {
                    collectedValues.Clear();
                    block.GetSlot(i).CollectNamedValues(collectedValues);
                    foreach (var arg in collectedValues)
                        if (arg.m_Value.IsValue(false)) // false as already reduced
                            uniforms.Add((VFXValue)arg.m_Value);
                }

            return uniforms;
        }

        public static HashSet<VFXValue> CollectAndRemoveSamplers(HashSet<VFXValue> uniforms)
        {
            HashSet<VFXValue> samplers = new HashSet<VFXValue>();

            // Collect samplers
            foreach (var param in uniforms)
                if (param.ValueType == VFXValueType.kTexture2D || param.ValueType == VFXValueType.kTexture3D)
                    samplers.Add(param);

            // Remove samplers from uniforms
            foreach (var param in samplers)
                uniforms.Remove(param);

            return samplers;
        }

        public static HashSet<VFXValue> CollectAndRemoveSignals(HashSet<VFXValue> uniforms)
        {
            HashSet<VFXValue> signals = new HashSet<VFXValue>();

            // Collect samplers
            foreach (var param in uniforms)
                if (param.ValueType == VFXValueType.kColorGradient || param.ValueType == VFXValueType.kCurve)
                    signals.Add(param);

            // Remove samplers from uniforms
            //foreach (var param in signals)
            //   uniforms.Remove(param);

            return signals;
        }

        public static HashSet<VFXValue> CollectIntersection(HashSet<VFXValue> params0,HashSet<VFXValue> params1)
        {
            HashSet<VFXValue> globalParams = new HashSet<VFXValue>();

            foreach (VFXValue param in params0)
                if (params1.Contains(param))
                    globalParams.Add(param);

            foreach (VFXValue param in globalParams)
            {
                params0.Remove(param);
                params1.Remove(param);
            }

            return globalParams;
        }

        public static void GenerateParamNames(Dictionary<VFXValue, string> paramToName, HashSet<VFXValue> parameters, string name)
        {
            int counter = 0;
            foreach (var param in parameters)
                if (!paramToName.ContainsKey(param))
                {
                    string fullName = name + counter;
                    paramToName.Add(param, fullName);
                    ++counter;
                }
        }

        // Collect all attributes from blocks and fills them in attribs
        public static void CollectAttributes(Dictionary<VFXAttribute, int> attribs, List<VFXBlockModel> blocks, int index)
        {
            foreach (VFXBlockModel block in blocks)
                for (int i = 0; i < block.Desc.Attributes.Length; ++i)
                {
                    VFXAttribute attr = block.Desc.Attributes[i];
                    int usage;
                    attribs.TryGetValue(attr, out usage);
                    int currentUsage = (0x1 | (attr.m_Writable ? 0x2 : 0x0)) << (index * 2);
                    attribs[attr] = usage | currentUsage;
                }
        }

        private static string WriteComputeShader(ShaderMetaData data,VFXShaderGeneratorModule initGenerator,VFXShaderGeneratorModule updateGenerator)
        {
            const int NB_THREAD_PER_GROUP = 256;

            bool hasInit = initGenerator != null; //data.initBlocks.Count > 0;
            bool hasUpdate = updateGenerator != null; //data.updateBlocks.Count > 0;

            ShaderSourceBuilder builder = new ShaderSourceBuilder();

            if (hasInit)
                builder.WriteLine("#pragma kernel CSVFXInit");
            if (hasUpdate)
                builder.WriteLine("#pragma kernel CSVFXUpdate");
            builder.WriteLine();

            builder.WriteLine("#include \"UnityCG.cginc\"");
            builder.WriteLine("#include \"HLSLSupport.cginc\"");
            builder.WriteLine();

            builder.Write("#define NB_THREADS_PER_GROUP ");
            builder.Write(NB_THREAD_PER_GROUP);
            builder.WriteLine();
            builder.WriteLine();

            // define semantics
            builder.WriteLine("#define RAND rand(seed)");
            builder.WriteLine("#define RAND2 float2(RAND,RAND)");
            builder.WriteLine("#define RAND3 float3(RAND,RAND,RAND)");
            builder.WriteLine("#define RAND4 float4(RAND,RAND,RAND,RAND)");
            builder.WriteLine("#define KILL {kill = true;}");
            builder.WriteLine("#define SAMPLE sampleSignal");
            builder.WriteLine();

            builder.WriteLine("CBUFFER_START(GlobalInfo)");
            builder.WriteLine("\tfloat deltaTime;");
            builder.WriteLine("\tfloat totalTime;");
            builder.WriteLine("\tuint nbMax;");
            if (data.hasRand)
                builder.WriteLine("\tuint systemSeed;");
            builder.WriteLine("CBUFFER_END");
            builder.WriteLine();

            if (hasInit)
            {
                builder.WriteLine("CBUFFER_START(SpawnInfo)");
                builder.WriteLine("\tuint nbSpawned;");
                builder.WriteLine("\tuint spawnIndex;");
                builder.WriteLine("CBUFFER_END");
                builder.WriteLine();
            } 

            // Uniforms buffer
            builder.WriteCBuffer("GlobalUniforms", data.globalUniforms, data.paramToName);
            builder.WriteCBuffer("initUniforms", data.initUniforms, data.paramToName);
            builder.WriteCBuffer("updateUniforms", data.updateUniforms, data.paramToName);

            // Write samplers
            builder.WriteSamplers(data.globalSamplers, data.paramToName);
            builder.WriteSamplers(data.initSamplers, data.paramToName);
            builder.WriteSamplers(data.updateSamplers, data.paramToName);

            // Write generated texture samplers
            if (data.generatedTextureData.HasColorTexture())
            {
                builder.WriteLine("sampler2D gradientTexture;");
                builder.WriteLine();
            }
            if (data.generatedTextureData.HasFloatTexture())
            {
                builder.WriteLine("sampler2D curveTexture;");
                builder.WriteLine();
            }

            // Write attribute struct
            foreach (var attribBuffer in data.attributeBuffers)
                builder.WriteAttributeBuffer(attribBuffer);

            // Write attribute buffer
            foreach (var attribBuffer in data.attributeBuffers)
            {
                builder.Write("RWStructuredBuffer<Attribute");
                builder.Write(attribBuffer.Index);
                builder.Write("> attribBuffer");
                builder.Write(attribBuffer.Index);
                builder.WriteLine(";");

                if (attribBuffer.Used(VFXContextDesc.Type.kTypeUpdate) && !attribBuffer.Writable(VFXContextDesc.Type.kTypeUpdate))
                {
                    builder.Write("StructuredBuffer<Attribute");
                    builder.Write(attribBuffer.Index);
                    builder.Write("> attribBuffer");
                    builder.Write(attribBuffer.Index);
                    builder.WriteLine("_RO;");
                }
            }
            if (data.attributeBuffers.Count > 0)
                builder.WriteLine();

            // Write deadlists
            if (data.hasKill)
            {
                builder.WriteLine("RWStructuredBuffer<int> flags;");
                builder.WriteLine("ConsumeStructuredBuffer<uint> deadListIn;");
                builder.WriteLine("AppendStructuredBuffer<uint> deadListOut;");
                builder.WriteLine("Buffer<uint> deadListCount; // This is bad to use a SRV to fetch deadList count but Unity API currently prevent from copying to CB");
                builder.WriteLine();
            }

            // Write functions
            if (data.hasRand)
            {
                builder.WriteLine("float rand(inout uint seed)");
                builder.EnterScope();
                builder.WriteLine("seed = 1664525 * seed + 1013904223;");
                builder.WriteLine("return float(seed) / 4294967296.0;");
                builder.ExitScope();
                builder.WriteLine();

                // XOR Style 
                /*
                builder.WriteLine("float rand(inout uint seed)");
                builder.EnterScope();
                builder.WriteLine("seed ^= (seed << 13);");
                builder.WriteLine("seed ^= (seed >> 17);");
                builder.WriteLine("seed ^= (seed << 5);");
                builder.WriteLine("return float(seed) / 4294967296.0;");
                builder.ExitScope();
                builder.WriteLine();
                */
            }

            if (data.generatedTextureData.HasColorTexture())
            {
                data.generatedTextureData.WriteSampleGradientFunction(builder);
                builder.WriteLine();
            }

            if (data.generatedTextureData.HasFloatTexture())
            {
                data.generatedTextureData.WriteSampleCurveFunction(builder);
                builder.WriteLine();
            }

            var functionNames = new HashSet<string>();
            foreach (var block in data.initBlocks)
                builder.WriteFunction(block, functionNames, data.generatedTextureData);
            foreach (var block in data.updateBlocks)
                builder.WriteFunction(block, functionNames, data.generatedTextureData);

            if (initGenerator != null)
                initGenerator.WriteFunctions(builder, data);
            if (updateGenerator != null)
                updateGenerator.WriteFunctions(builder, data);

            bool HasPhaseShift = VFXEditor.AssetModel.PhaseShift;

            // Write init kernel
            if (hasInit)
            {
                builder.WriteKernelHeader("CSVFXInit");
                if (data.hasKill)
                    builder.WriteLine("if (id.x < min(nbSpawned,deadListCount[0]))");
                else
                    builder.WriteLine("if (id.x < nbSpawned)");
                builder.EnterScope();
                if (data.hasKill)
                    builder.WriteLine("uint index = deadListIn.Consume();");
                else
                    builder.WriteLine("uint index = id.x + spawnIndex;");
                builder.WriteLine();

                foreach (var attribBuffer in data.attributeBuffers)
                {
                    builder.Write("Attribute");
                    builder.Write(attribBuffer.Index);
                    builder.Write(" attrib");
                    builder.Write(attribBuffer.Index);              

                    // TODO tmp
                    // Initialize to avoid warning as error while compiling
                    builder.Write(" = (Attribute");
                    builder.Write(attribBuffer.Index);
                    builder.WriteLine(")0;");
                }
                builder.WriteLine();

                // Init random
                if (data.hasRand)
                {
                    // Find rand attribute
                    builder.WriteLine("uint seed = (id.x + spawnIndex) ^ systemSeed;");
                    builder.WriteLine("seed = (seed ^ 61) ^ (seed >> 16);");
                    builder.WriteLine("seed *= 9;");
                    builder.WriteLine("seed = seed ^ (seed >> 4);");
                    builder.WriteLine("seed *= 0x27d4eb2d;");
                    builder.WriteLine("seed = seed ^ (seed >> 15);");
                    builder.WriteAttrib(CommonAttrib.Seed, data);
                    builder.WriteLine(" = seed;");
                    builder.WriteLine();
                }

                // Init phase
                if (HasPhaseShift)
                {
                    builder.WriteAttrib(CommonAttrib.Phase, data);
                    builder.Write(" = rand(");
                    builder.WriteAttrib(CommonAttrib.Seed, data);
                    builder.WriteLine(");");
                    builder.WriteLine();
                }

                initGenerator.WritePreBlock(builder, data);
                
                foreach (var block in data.initBlocks)
                    builder.WriteFunctionCall(block, functionNames, data);
                builder.WriteLine();

                initGenerator.WritePostBlock(builder, data);

                // Remove phase shift
                if (HasPhaseShift)
                {
                    builder.WriteRemovePhaseShift(data);
                    builder.WriteLine();
                }

                foreach (var attribBuffer in data.attributeBuffers)
                {
                    builder.Write("attribBuffer");
                    builder.Write(attribBuffer.Index);
                    builder.Write("[index] = attrib");
                    builder.Write(attribBuffer.Index);
                    builder.WriteLine(";");
                }

                if (data.hasKill)
                {
                    builder.WriteLine();
                    builder.WriteLine("flags[index] = 1;");
                }

                builder.ExitScope();
                builder.ExitScope();
                builder.WriteLine();
            }

            // Write update kernel
            if (hasUpdate)
            {
                builder.WriteKernelHeader("CSVFXUpdate");

                builder.Write("if (id.x < nbMax");
                if (data.hasKill)
                    builder.WriteLine(" && flags[id.x] == 1)");
                else
                    builder.WriteLine(")");
                builder.EnterScope();
                builder.WriteLine("uint index = id.x;");

                if (data.hasKill)
                    builder.WriteLine("bool kill = false;");

                builder.WriteLine();
         
                foreach (var attribBuffer in data.attributeBuffers)
                {
                    if (attribBuffer.Used(VFXContextDesc.Type.kTypeUpdate))
                    {
                        builder.Write("Attribute");
                        builder.Write(attribBuffer.Index);
                        builder.Write(" attrib");
                        builder.Write(attribBuffer.Index);
                        builder.Write(" = attribBuffer");
                        builder.Write(attribBuffer.Index);
                        if (!attribBuffer.Writable(VFXContextDesc.Type.kTypeUpdate))
                            builder.Write("_RO");
                        builder.WriteLine("[index];");
                    }
                }
                builder.WriteLine();

                // Add phase shift
                if (HasPhaseShift)
                {
                    builder.WriteAddPhaseShift(data);
                    builder.WriteLine();
                }

                updateGenerator.WritePreBlock(builder, data);

                foreach (var block in data.updateBlocks)
                    builder.WriteFunctionCall(block, functionNames, data);
                builder.WriteLine();

                updateGenerator.WritePostBlock(builder, data);

                // Remove phase shift
                if (HasPhaseShift)
                {
                    builder.WriteRemovePhaseShift(data);
                    builder.WriteLine();
                }

                if (data.hasKill)
                {
                    builder.WriteLine("if (kill)");
                    builder.EnterScope();
                    builder.WriteLine("flags[index] = 0;");
                    builder.WriteLine("deadListOut.Append(index);");
                    builder.WriteLine("return;");
                    builder.ExitScope();
                    builder.WriteLine();
                }

                foreach (var attribBuffer in data.attributeBuffers)
                {
                    if (attribBuffer.Writable(VFXContextDesc.Type.kTypeUpdate))
                    {
                        builder.Write("attribBuffer");
                        builder.Write(attribBuffer.Index);
                        builder.Write("[index] = attrib");
                        builder.Write(attribBuffer.Index);
                        builder.WriteLine(";");
                    }
                }

                builder.ExitScope();
                builder.ExitScope();
                builder.WriteLine();
            }

            return builder.ToString();
        }

        private static string WriteOutputShader(VFXSystemModel system, ShaderMetaData data, VFXOutputShaderGeneratorModule outputGenerator)
        {
            ShaderSourceBuilder builder = new ShaderSourceBuilder();

            builder.Write("Shader \"Custom/VFX_");
            builder.Write(system.Id);
            builder.WriteLine("\"");
            builder.EnterScope();
            builder.WriteLine("SubShader");
            builder.EnterScope();

            BlendMode blendMode = system.BlendingMode;

            if (blendMode != BlendMode.kMasked)
                builder.WriteLine("Tags { \"Queue\"=\"Transparent\" \"IgnoreProjector\"=\"True\" \"RenderType\"=\"Transparent\" }");
            builder.WriteLine("Pass");
            builder.EnterScope();
            if (blendMode == BlendMode.kAdditive)
                builder.WriteLine("Blend SrcAlpha One");
            else if (blendMode == BlendMode.kAlpha)
                builder.WriteLine("Blend SrcAlpha OneMinusSrcAlpha");
            builder.WriteLine("ZTest LEqual");
            if (blendMode == BlendMode.kMasked)
                builder.WriteLine("ZWrite On");
            else
                builder.WriteLine("ZWrite Off");
            builder.WriteLine("CGPROGRAM");
            builder.WriteLine("#pragma target 5.0");
            builder.WriteLine();
            builder.WriteLine("#pragma vertex vert");
            builder.WriteLine("#pragma fragment frag");
            builder.WriteLine();
            builder.WriteLine("#include \"UnityCG.cginc\"");
            builder.WriteLine();

            builder.WriteCBuffer("outputUniforms", data.outputUniforms, data.outputParamToName);
            builder.WriteSamplers(data.outputSamplers, data.outputParamToName);

            foreach (AttributeBuffer buffer in data.attributeBuffers)
                if (buffer.Used(VFXContextDesc.Type.kTypeOutput))
                    builder.WriteAttributeBuffer(buffer);

            foreach (AttributeBuffer buffer in data.attributeBuffers)
                if (buffer.Used(VFXContextDesc.Type.kTypeOutput))
                {
                    builder.Write("StructuredBuffer<Attribute");
                    builder.Write(buffer.Index);
                    builder.Write("> attribBuffer");
                    builder.Write(buffer.Index);
                    builder.WriteLine(";");
                }

            if (data.hasKill)
                builder.WriteLine("StructuredBuffer<int> flags;");

            builder.WriteLine();
            builder.WriteLine("struct ps_input");
            builder.EnterScope();
            builder.WriteLine("float4 pos : SV_POSITION;");

            bool hasColor = data.attribToBuffer.ContainsKey(CommonAttrib.Color);
            bool hasAlpha = data.attribToBuffer.ContainsKey(CommonAttrib.Alpha);

            if (hasColor || hasAlpha)
                builder.WriteLine("nointerpolation float4 col : COLOR0;");

            outputGenerator.WriteAdditionalVertexOutput(builder, data);

            builder.ExitScopeStruct();
            builder.WriteLine();

            outputGenerator.WriteFunctions(builder, data);

            builder.WriteLine("ps_input vert (uint id : SV_VertexID, uint instanceID : SV_InstanceID)");
            builder.EnterScope();
            builder.WriteLine("ps_input o;");

            outputGenerator.WriteIndex(builder, data);

            if (data.hasKill)
            {
                builder.WriteLine("if (flags[index] == 1)");
                builder.EnterScope();
            }

            foreach (var buffer in data.attributeBuffers)
                if (buffer.Used(VFXContextDesc.Type.kTypeOutput))
                {
                    builder.Write("Attribute");
                    builder.Write(buffer.Index);
                    builder.Write(" attrib");
                    builder.Write(buffer.Index);
                    builder.Write(" = attribBuffer");
                    builder.Write(buffer.Index);
                    builder.WriteLine("[index];");
                }
            builder.WriteLine();

            outputGenerator.WritePreBlock(builder, data);
            outputGenerator.WritePostBlock(builder, data);

            if (hasColor || hasAlpha)
            {
                builder.Write("o.col = float4(");

                if (hasColor)
                {
                    builder.WriteAttrib(CommonAttrib.Color, data);
                    builder.Write(".xyz,");
                }
                else
                    builder.Write("1.0,1.0,1.0,");

                if (hasAlpha)
                {
                    builder.WriteAttrib(CommonAttrib.Alpha, data);
                    builder.WriteLine(");");
                }
                else
                    builder.WriteLine("0.5);");
            }

            if (data.hasKill)
            {
                // clip the vertex if not alive
                builder.ExitScope();
                builder.WriteLine("else");
                builder.EnterScope();
                builder.WriteLine("o.pos = -1.0;");

                if (hasColor)
                    builder.WriteLine("o.col = 0;");

                builder.ExitScope();
                builder.WriteLine();
            }

            builder.WriteLine("return o;");
            builder.ExitScope();
            builder.WriteLine();
            builder.WriteLine("float4 frag (ps_input i) : COLOR");
            builder.EnterScope();

            if (hasColor || hasAlpha)
                builder.WriteLine("float4 color = i.col;");
            else
                builder.WriteLine("float4 color = float4(1.0,1.0,1.0,0.5);");

            outputGenerator.WritePixelShader(system, builder, data);

            builder.WriteLine("return color;");

            builder.ExitScope();
            builder.WriteLine();
            builder.WriteLine("ENDCG");
            builder.ExitScope();
            builder.ExitScope();
            builder.WriteLine("FallBack Off");
            builder.ExitScope();

            return builder.ToString();
        }
    }
}