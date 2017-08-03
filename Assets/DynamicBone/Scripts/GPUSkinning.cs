using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections;
using System.Collections.Generic;

public class GPUSkinning : MonoBehaviour 
{
	public SkinnedMeshRenderer smr = null;

	private Mesh mesh = null;

	private GPUSkinning_Bone[] bones = null;

	private int rootBoneIndex = 0;

	private MeshFilter mf = null;

	private MeshRenderer mr = null;

	private Material newMtrl = null;

	private Mesh newMesh = null;

    private Matrix4x4[] matricesUniformBlock = null;

    private int shaderPropID_Matrices = 0;

    public Transform root = null;

	private void Start () 
	{
        shaderPropID_Matrices = Shader.PropertyToID("_Matrices");

		smr = GetComponentInChildren<SkinnedMeshRenderer>();
		mesh = smr.sharedMesh;

        // Init Bones
		int numBones = smr.bones.Length;
		bones = new GPUSkinning_Bone[numBones];
		for(int i = 0; i < numBones; ++i)
		{
			GPUSkinning_Bone bone = new GPUSkinning_Bone();
			bones[i] = bone;
			bone.transform = smr.bones[i];
			bone.bindpose = mesh.bindposes[i]/*smr to bone*/;
		}

        matricesUniformBlock = new Matrix4x4[numBones];

        // Construct Bones' Hierarchy
        for(int i = 0; i < numBones; ++i)
        {
            if(bones[i].transform == smr.rootBone)
            {
                rootBoneIndex = i;
                break;
            }
        }
        System.Action<GPUSkinning_Bone> CollectChildren = null;
        CollectChildren = (currentBone) =>
        {
            List<GPUSkinning_Bone> children = new List<GPUSkinning_Bone>();
            for (int j = 0; j < currentBone.transform.childCount; ++j)
            {
                Transform childTransform = currentBone.transform.GetChild(j);
                GPUSkinning_Bone childBone = GetBoneByTransform(childTransform);
                if (childBone != null)
                {
                    childBone.parent = currentBone;
                    children.Add(childBone);
                    CollectChildren(childBone);
                }
            }
            currentBone.children = children.ToArray();
        };
        CollectChildren(bones[rootBoneIndex]);

        // New MeshFilter MeshRenderer
		mf = gameObject.AddComponent<MeshFilter>();
		mr = gameObject.AddComponent<MeshRenderer>();

		newMtrl = new Material(Shader.Find("Unlit/GPUSkinning"));
        newMtrl.CopyPropertiesFromMaterial(smr.sharedMaterial);
		mr.sharedMaterial = newMtrl;

        // Fetch bone-weight storing as tangents
        int numVertices = mesh.vertexCount;
        Vector4[] tangents = new Vector4[mesh.vertexCount];
        Vector4[] uv2 = new Vector4[numVertices];
        Vector4[] uv3 = new Vector4[numVertices];

        for (int i = 0; i < numVertices; ++i)
        {
            BoneWeight boneWeight = mesh.boneWeights[i];
            tangents[i].x = boneWeight.boneIndex0;
            tangents[i].y = boneWeight.weight0;
            tangents[i].z = boneWeight.boneIndex1;
            tangents[i].w = boneWeight.weight1;

            
            Vector4 skinData_01 = new Vector4();
            skinData_01.x = boneWeight.boneIndex0;
            skinData_01.y = boneWeight.weight0;
            skinData_01.z = boneWeight.boneIndex1;
            skinData_01.w = boneWeight.weight1;
            uv2[i] = skinData_01;

            Vector4 skinData_23 = new Vector4();
            skinData_23.x = boneWeight.boneIndex2;
            skinData_23.y = boneWeight.weight2;
            skinData_23.z = boneWeight.boneIndex3;
            skinData_23.w = boneWeight.weight3;
            uv3[i] = skinData_23;
            
        }       

        // New Mesh
        newMesh = new Mesh();
		newMesh.vertices = mesh.vertices;
        newMesh.normals = mesh.normals;
        newMesh.tangents = tangents;
		newMesh.uv = mesh.uv;
		newMesh.triangles = mesh.triangles;
		mf.sharedMesh = newMesh;

        newMesh.SetUVs(1, new List<Vector4>(uv2));
        newMesh.SetUVs(2, new List<Vector4>(uv3));

        /*
        // Fetch animations' data
#if UNITY_EDITOR
        int boneAnimationsCount = 0;
		boneAnimations = new GPUSkinning_BoneAnimation[GetComponent<Animator>().runtimeAnimatorController.animationClips.Length];
		foreach(AnimationClip animClip in GetComponent<Animator>().runtimeAnimatorController.animationClips)
		{
			GPUSkinning_BoneAnimation boneAnimation = ScriptableObject.CreateInstance<GPUSkinning_BoneAnimation>();
			boneAnimation.fps = 30;
			boneAnimation.animName = animClip.name;
			boneAnimation.frames = new GPUSkinning_BoneAnimationFrame[(int)(animClip.length * boneAnimation.fps)];
			boneAnimation.length = animClip.length;
			boneAnimations[boneAnimationsCount++] = boneAnimation;

			for(int frameIndex = 0; frameIndex < boneAnimation.frames.Length; ++frameIndex)
			{
				GPUSkinning_BoneAnimationFrame frame = new GPUSkinning_BoneAnimationFrame();
				boneAnimation.frames[frameIndex] = frame;
				float second = (float)(frameIndex) / (float)boneAnimation.fps;

				List<GPUSkinning_Bone> bones2 = new List<GPUSkinning_Bone>();
				List<Matrix4x4> matrices = new List<Matrix4x4>();
                List<string> bonesHierarchyNames = new List<string>();
                EditorCurveBinding[] curvesBinding = AnimationUtility.GetCurveBindings(animClip);
				foreach(var curveBinding in curvesBinding)
				{
					GPUSkinning_Bone bone = GetBoneByHierarchyName(curveBinding.path);

					if(bones2.Contains(bone))
					{
						continue;
					}
					bones2.Add(bone);

                    bonesHierarchyNames.Add(GetBoneHierarchyName(bone));

                    AnimationCurve curveRX = AnimationUtility.GetEditorCurve(animClip, curveBinding.path, curveBinding.type, "m_LocalRotation.x");
					AnimationCurve curveRY = AnimationUtility.GetEditorCurve(animClip, curveBinding.path, curveBinding.type, "m_LocalRotation.y");
					AnimationCurve curveRZ = AnimationUtility.GetEditorCurve(animClip, curveBinding.path, curveBinding.type, "m_LocalRotation.z");
					AnimationCurve curveRW = AnimationUtility.GetEditorCurve(animClip, curveBinding.path, curveBinding.type, "m_LocalRotation.w");
					
					AnimationCurve curvePX = AnimationUtility.GetEditorCurve(animClip, curveBinding.path, curveBinding.type, "m_LocalPosition.x");
					AnimationCurve curvePY = AnimationUtility.GetEditorCurve(animClip, curveBinding.path, curveBinding.type, "m_LocalPosition.y");
					AnimationCurve curvePZ = AnimationUtility.GetEditorCurve(animClip, curveBinding.path, curveBinding.type, "m_LocalPosition.z");

					float curveRX_v = curveRX.Evaluate(second);
					float curveRY_v = curveRY.Evaluate(second);
					float curveRZ_v = curveRZ.Evaluate(second);
					float curveRW_v = curveRW.Evaluate(second);

					float curvePX_v = curvePX.Evaluate(second);
					float curvePY_v = curvePY.Evaluate(second);
					float curvePZ_v = curvePZ.Evaluate(second);

					Vector3 translation = new Vector3(curvePX_v, curvePY_v, curvePZ_v);
					Quaternion rotation = new Quaternion(curveRX_v, curveRY_v, curveRZ_v, curveRW_v);
					NormalizeQuaternion(ref rotation);
					matrices.Add(
						Matrix4x4.TRS(translation, rotation, Vector3.one)
					);
				}

				frame.bones = bones2.ToArray();
				frame.matrices = matrices.ToArray();
                frame.bonesHierarchyNames = bonesHierarchyNames.ToArray();
			}
		}
        AssetDatabase.CreateAsset(boneAnimations[0], "Assets/GPUSkinning/Resources/anim0.asset");
        AssetDatabase.Refresh();
#else
        boneAnimations = new GPUSkinning_BoneAnimation[] { Resources.Load("anim0") as GPUSkinning_BoneAnimation };
        foreach(var boneAnimation in boneAnimations)
        {
            foreach(var frame in boneAnimation.frames)
            {
                int numBones2 = frame.bonesHierarchyNames.Length;
                frame.bones = new GPUSkinning_Bone[numBones2];
                for(int i = 0; i < numBones2; ++i)
                {
                    frame.bones[i] = GetBoneByHierarchyName(frame.bonesHierarchyNames[i]);
                }
            }
        }
#endif


        GameObject.Destroy(transform.Find("pelvis").gameObject);
        GameObject.Destroy(transform.Find("mutant_mesh").gameObject);
        Object.Destroy(gameObject.GetComponent<Animator>());
*/
        //smr.enabled = false;

        //PrintBones();
    }

    
	private float second = 0.0f;
	private void Update()
	{
		//UpdateBoneAnimationMatrix(null, second);
		Play();
		//second += Time.deltaTime;
	}
    

    public void UpdateBones(List<DynamicBone.Particle> particles)
    {
        Matrix4x4 rootMatrix = Matrix4x4.TRS(gameObject.transform.position, gameObject.transform.rotation, Vector3.one);

        for (int i = 0; i < particles.Count; ++i)
        {
            Quaternion rotation = particles[i].m_Transform.rotation;
            NormalizeQuaternion(ref rotation);
            matricesUniformBlock[i] = rootMatrix.inverse * Matrix4x4.TRS(particles[i].m_Transform.position, rotation, Vector3.one)  * bones[i].bindpose;
        }
    }

	public void Play()
	{    
        /*    
        for (int i = 0; i < bones.Length; ++i)
        {

            Matrix4x4 rootMatrix = Matrix4x4.TRS(gameObject.transform.position, gameObject.transform.rotation, Vector3.one);

            matricesUniformBlock[i] = Matrix4x4.TRS(bones[i].transform.position, bones[i].transform.rotation, Vector3.one)* bones[i].bindpose * rootMatrix.inverse;
        }
        */

        newMtrl.SetMatrixArray(shaderPropID_Matrices, matricesUniformBlock);
    }

	private void OnDestroy()
	{
		if(newMtrl != null)
		{
			Object.Destroy(newMtrl);
		}
		if(newMesh != null)
		{
			Object.Destroy(newMesh);
		}
	}

	private GPUSkinning_Bone GetBoneByTransform(Transform transform)
	{
		foreach(GPUSkinning_Bone bone in bones)
		{
			if(bone.transform == transform)
			{
				return bone;
			}
		}
		return null;
	}

	private GPUSkinning_Bone GetBoneByHierarchyName(string hierarchyName)
	{
		System.Func<GPUSkinning_Bone, string, GPUSkinning_Bone> Search = null;
		Search = (bone, name) => 
		{
			if(name == hierarchyName)
			{
				return bone;
			}
			foreach(GPUSkinning_Bone child in bone.children)
			{
				GPUSkinning_Bone result = Search(child, name + "/" + child.name);
				if(result != null)
				{
					return result;
				}
			}
			return null;
		};

		return Search(bones[rootBoneIndex], bones[rootBoneIndex].name);
	}

    private string GetBoneHierarchyName(GPUSkinning_Bone bone)
    {
        string str = string.Empty;

        GPUSkinning_Bone currentBone = bone;
        while(currentBone != null)
        {
            if(str == string.Empty)
            {
                str = currentBone.name;
            }
            else
            {
                str = currentBone.name + "/" + str;
            }

            currentBone = currentBone.parent;
        }

        return str;
    }

	private void PrintBones()
	{
		string text = string.Empty;

		System.Action<GPUSkinning_Bone, string> PrintBone = null;
		PrintBone = (bone, prefix) => 
		{
			text += prefix + bone.transform.gameObject.name + "\n";
			prefix += "    ";
			foreach(var childBone in bone.children)
			{
				PrintBone(childBone, prefix);
			}
		};

		PrintBone(bones[rootBoneIndex], string.Empty);

		Debug.LogError(text);
	}

	private void NormalizeQuaternion (ref Quaternion q)
	{
	    float sum = 0;
	    for (int i = 0; i < 4; ++i)
	        sum += q[i] * q[i];
	    float magnitudeInverse = 1 / Mathf.Sqrt(sum);
	    for (int i = 0; i < 4; ++i)
	        q[i] *= magnitudeInverse;  
	}
}
