using InnoVault.Models3D.Runtime;

namespace InnoVault.Models3D.Wavefront
{
    /// <summary>
    /// 表示 MTL 文件中描述的一份材质
    /// <br/>本 MVP 仅消费 <c>Kd</c>、<c>map_Kd</c>、<c>d</c>/<c>Tr</c> 字段，其它字段被忽略
    /// <br/>除导入数据外，材质还可挂载若干"材质级覆盖项"：<see cref="Model3DMaterial.Effect"/> / <see cref="Model3DMaterial.EffectProvider"/>
    /// / <see cref="Model3DMaterial.ConfigureEffect"/> / <see cref="Model3DMaterial.RenderStateOverride"/> / <see cref="Model3DMaterial.PreDrawGroup"/> / <see cref="Model3DMaterial.PostDrawGroup"/>，
    /// 用于"所有使用该材质的 mesh group 都套上同一份 shader 或状态覆盖"的场景
    /// <br/>这些覆盖项的优先级<b>低于</b> <see cref="Runtime.Model3DInstance"/> 上的同名字段
    /// </summary>
    public sealed class ObjMaterial : Model3DMaterial
    {
        /// <summary>
        /// 构造一个新的材质，名字一旦确定即不可更改
        /// </summary>
        public ObjMaterial(string name) : base(name) { }
    }
}
