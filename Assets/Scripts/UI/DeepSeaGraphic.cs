using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>原创海流、海床与航标线稿。UI网格随视口缩放，无贴图/材质依赖。</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DeepSeaGraphic : MaskableGraphic
    {
        public enum Shape { Ocean, Frame, Beacon, Battle, Shop, Empty, Final, Equipment, Close, Pause, Position, Lock }
        public Shape shape;
        public int variant;
        protected override void Awake() { base.Awake(); raycastTarget = false; }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Vector2 P(float x, float y) => new Vector2(r.xMin + x*r.width, r.yMin + y*r.height);
            if (shape == Shape.Ocean)
            {
                Quad(vh, P(0,0), P(0,1), P(1,1), P(1,0), new Color(.018f,.049f,.075f), new Color(.055f,.16f,.19f));
                // 斜射光仅用于环境，不覆盖任何交互命中区。
                for(int i=0;i<5;i++)
                    Quad(vh,P(.05f+i*.19f,1),P(.12f+i*.19f,1),P(.52f+i*.14f,0),P(.42f+i*.14f,0),new Color(.35f,.8f,.79f,.035f),new Color(.2f,.6f,.7f,0));
                for(int band=0;band<12;band++)
                {
                    Vector2 last=P(0,0);
                    for(int j=0;j<=56;j++)
                    {
                        float x=j/56f;
                        float y=.055f+band*.025f + .052f*Mathf.Sin(x*7+band*.15f)+.019f*Mathf.Sin(x*17+band*.26f);
                        Vector2 now=P(x,y);
                        if(j>0) Line(vh,last,now,1.3f,new Color(.29f,.62f,.65f,.13f));
                        last=now;
                    }
                }
                // 中央留白，细颗粒主要分布在两侧。
                for(int i=0;i<78;i++)
                {
                    float x=Mathf.Repeat(i*.6180339f,1), y=Mathf.Repeat(i*.381966f+.13f,1);
                    if(x>.22f && x<.78f) continue;
                    Vector2 speck=P(x,y); float size=i%4==0?2:1;
                    Line(vh,speck-Vector2.up*size,speck+Vector2.up*size,1,new Color(.62f,.86f,.82f,.24f));
                }
                return;
            }
            Color c=color;
            if(shape==Shape.Position)
            {
                vh.AddVert(P(.5f,0),c,Vector2.zero); vh.AddVert(P(0,1),c,Vector2.zero); vh.AddVert(P(1,1),c,Vector2.zero);
                vh.AddTriangle(0,1,2); return;
            }
            if(shape==Shape.Lock)
            {
                Line(vh,P(.2f,.1f),P(.8f,.1f),2,c); Line(vh,P(.2f,.1f),P(.2f,.6f),2,c);
                Line(vh,P(.8f,.1f),P(.8f,.6f),2,c); Line(vh,P(.2f,.6f),P(.8f,.6f),2,c);
                Line(vh,P(.35f,.6f),P(.35f,.9f),2,c); Line(vh,P(.35f,.9f),P(.65f,.9f),2,c); Line(vh,P(.65f,.9f),P(.65f,.6f),2,c); return;
            }
            if(shape==Shape.Close)
            {
                Line(vh,P(.35f,.3f),P(.65f,.7f),2,c);Line(vh,P(.65f,.3f),P(.35f,.7f),2,c);return;
            }
            if(shape==Shape.Pause)
            {
                Line(vh,P(.4f,.28f),P(.4f,.72f),4,c);Line(vh,P(.6f,.28f),P(.6f,.72f),4,c);return;
            }
            if(shape==Shape.Frame)
            {
                Line(vh,P(0,0),P(1,0),1,c); Line(vh,P(0,1),P(1,1),1,c);
                Line(vh,P(0,0),P(0,1),1,c); Line(vh,P(1,0),P(1,1),1,c);
                return;
            }
            Vector2 center=P(.5f,.5f); float radius=Mathf.Min(r.width,r.height)*.4f;
            for(int i=0;i<4;i++)
            {
                float a=(45+i*90)*Mathf.Deg2Rad,b=(45+(i+1)*90)*Mathf.Deg2Rad;
                Line(vh,center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,1.6f,c);
            }
            if(shape==Shape.Battle || shape==Shape.Final)
            {
                Line(vh,P(.3f,.27f),P(.7f,.73f),3,c); Line(vh,P(.7f,.27f),P(.3f,.73f),3,c);
                if(shape==Shape.Final) Line(vh,P(.3f,.85f),P(.7f,.85f),3,c);
            }
            else if(shape==Shape.Shop)
            {
                Line(vh,P(.28f,.58f),P(.72f,.58f),3,c);
                Line(vh,P(.35f,.58f),P(.35f,.3f),2,c); Line(vh,P(.65f,.58f),P(.65f,.3f),2,c);
                Line(vh,P(.35f,.3f),P(.65f,.3f),2,c);
            }
            else if(shape==Shape.Empty)
                Line(vh,P(.38f,.5f),P(.62f,.5f),2,c);
            else
            {
                int spokes=shape==Shape.Equipment?3+variant%5:3;
                for(int i=0;i<spokes;i++)
                {
                    float a=(i*360f/spokes+90)*Mathf.Deg2Rad;
                    Line(vh,center,center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius*.65f,2,c);
                }
            }
        }
        private static void Line(VertexHelper vh,Vector2 a,Vector2 b,float width,Color c)
        {
            Vector2 d=b-a, n=new Vector2(-d.y,d.x).normalized*width*.5f;
            Quad(vh,a-n,a+n,b+n,b-n,c,c);
        }
        private static void Quad(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color bottom,Color top)
        {
            int i=vh.currentVertCount;
            vh.AddVert(a,bottom,Vector2.zero);vh.AddVert(b,top,Vector2.zero);
            vh.AddVert(c,top,Vector2.zero);vh.AddVert(d,bottom,Vector2.zero);
            vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
        }
    }
}
