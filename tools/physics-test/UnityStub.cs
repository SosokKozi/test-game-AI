// Минимальная заглушка UnityEngine: только то, что нужно CarSpec/TireModel/Drivetrain/Wheel вне движка.
using System;
namespace UnityEngine {
  public static class Mathf {
    public const float PI = (float)Math.PI, Deg2Rad = PI/180f, Rad2Deg = 180f/PI;
    public static float Sin(float x)=>(float)Math.Sin(x); public static float Cos(float x)=>(float)Math.Cos(x);
    public static float Tan(float x)=>(float)Math.Tan(x); public static float Atan(float x)=>(float)Math.Atan(x);
    public static float Sqrt(float x)=>(float)Math.Sqrt(x); public static float Abs(float x)=>Math.Abs(x);
    public static float Sign(float x)=>x>=0?1f:-1f; public static float Max(float a,float b)=>a>b?a:b; public static float Min(float a,float b)=>a<b?a:b;
    public static int Max(int a,int b)=>a>b?a:b; public static int Min(int a,int b)=>a<b?a:b;
    public static float Clamp(float v,float a,float b)=>v<a?a:v>b?b:v; public static int Clamp(int v,int a,int b)=>v<a?a:v>b?b:v;
    public static float Clamp01(float v)=>Clamp(v,0,1); public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
    public static float InverseLerp(float a,float b,float v)=>a!=b?Clamp01((v-a)/(b-a)):0;
    public static float MoveTowards(float c,float t,float d)=>Math.Abs(t-c)<=d?t:c+Math.Sign(t-c)*d;
    public static float Repeat(float t,float l)=>Clamp(t-(float)Math.Floor(t/l)*l,0,l);
    public static float PerlinNoise(float x,float y)=>0.5f;
  }
  public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
    public static Vector3 zero=>new Vector3(0,0,0); public static Vector3 up=>new Vector3(0,1,0); public static Vector3 forward=>new Vector3(0,0,1); public static Vector3 right=>new Vector3(1,0,0);
    public static Vector3 operator*(Vector3 a,float s)=>new Vector3(a.x*s,a.y*s,a.z*s); public static Vector3 operator+(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
    public static Vector3 operator-(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); public static Vector3 operator-(Vector3 a)=>new Vector3(-a.x,-a.y,-a.z);
    public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z; public static Vector3 Cross(Vector3 a,Vector3 b)=>new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
    public Vector3 normalized=>this; }
  public struct Quaternion { public static Quaternion AngleAxis(float a,Vector3 v)=>default; public static Vector3 operator*(Quaternion q,Vector3 v)=>v; }
  public struct Color { public float r,g,b,a; public Color(float r,float g,float b){this.r=r;this.g=g;this.b=b;a=1;} public static Color gray=>new Color(.5f,.5f,.5f);}
  public class Object { public int GetInstanceID()=>0; public HideFlags hideFlags; public static implicit operator bool(Object o)=>o!=null; }
  public enum HideFlags { HideAndDontSave }
  public enum QueryTriggerInteraction { Ignore }
  public class Component : Object { public T GetComponent<T>() where T:class=>null; }
  public class Behaviour : Component {} public class MonoBehaviour : Behaviour {}
  public class Collider : Component {}
  public class GameObject : Object { public GameObject(string n){} public T GetComponent<T>() where T:class=>null; public T AddComponent<T>() where T:new()=>new T(); }
  public class Transform : Component { public Vector3 up, forward; public Vector3 TransformPoint(Vector3 v)=>v; }
  public class Rigidbody : Component { public void AddForceAtPosition(Vector3 f,Vector3 p){} public Vector3 GetPointVelocity(Vector3 p)=>default; }
  public struct RaycastHit { public float distance; public Vector3 point, normal; public Collider collider; }
  public static class Physics { public static bool Raycast(Vector3 o,Vector3 d,out RaycastHit h,float l,int m,QueryTriggerInteraction q){h=default;return false;} }
  public class TextAsset : Object { public string text; }
  public static class Resources { public static T Load<T>(string p) where T:class=>null; }
  public static class JsonUtility { public static T FromJson<T>(string s)=>default; }
}
