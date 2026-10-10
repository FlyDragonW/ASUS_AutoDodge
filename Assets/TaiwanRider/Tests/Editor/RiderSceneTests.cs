using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
namespace TaiwanRider.Tests
{
    public class RiderSceneTests
    {
        const string Root="Assets/TaiwanRider/";
        Scene scene;
        bool opened;
        [OneTimeSetUp] public void LoadScene()
        {
            scene=SceneManager.GetSceneByPath(Root+"Scenes/GongguanRide.unity");
            if(!scene.IsValid() || !scene.isLoaded){scene=EditorSceneManager.OpenScene(Root+"Scenes/GongguanRide.unity",OpenSceneMode.Additive);opened=true;}
        }
        [OneTimeTearDown] public void CloseScene(){if(opened)EditorSceneManager.CloseScene(scene,true);}
        T[] All<T>() where T:Component => scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<T>(true)).ToArray();
        [Test] public void RoadReproducesSurveyAndHasContinuousHeading()
        {
            var route=All<RoadRoute>().Single();
            Assert.That(route.points.Length,Is.GreaterThan(10));
            Assert.That(route.CenterX(0),Is.EqualTo(0).Within(.01));
            Assert.That(route.CenterX(-76),Is.InRange(-12f,-10f));
            Assert.That(route.CenterX(70),Is.InRange(-.2f,.2f));
            for(float z=-76;z<76;z+=2)
                Assert.That(Quaternion.Angle(route.Heading(z),route.Heading(z+2)),Is.LessThan(15),"Discontinuous road at "+z);
        }
        [Test] public void ScooterImportedAtRealMetricWheelbase()
        {
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Art/Models/Scooter125.fbx");
            var transforms=model.GetComponentsInChildren<Transform>();
            var front=transforms.Single(x=>x.name=="FrontWheel");
            var rear=transforms.Single(x=>x.name=="RearWheel");
            Assert.That(Vector3.Distance(front.position,rear.position),Is.EqualTo(1.275).Within(.025));
        }
        [Test] public void PlayerPhysicsAndMirrorsAreWired()
        {
            var rider=All<ScooterController>().Single();
            Assert.That(rider.definition,Is.Not.Null);
            Assert.That(rider.frontWheel.radius,Is.EqualTo(.217).Within(.001));
            Assert.That(Vector3.Distance(rider.frontWheel.transform.position,rider.rearWheel.transform.position),Is.EqualTo(1.275).Within(.001));
            Assert.That(rider.riderCamera,Is.Not.Null);
            Assert.That(rider.session,Is.Not.Null);
            Assert.That(All<AudioListener>().Length,Is.EqualTo(1));
            var mirrors=All<MirrorView>();Assert.That(mirrors.Length,Is.EqualTo(2));
            foreach(var mirror in mirrors){Assert.That(mirror.surface,Is.Not.Null);Assert.That(mirror.mirrorCamera.cullingMask&(1<<8),Is.Zero);}
        }
        [Test] public void ScenarioActorsAndDefinitionsAreComplete()
        {
            var session=All<ScenarioRunner>().Single();
            Assert.That(session.scenarios.Select(x=>x.kind).Distinct().Count(),Is.EqualTo(3));
            Assert.That(session.car.bodyCollider,Is.Not.Null);
            Assert.That(session.scooter.bodyCollider,Is.Not.Null);
            Assert.That(session.startPosition.z,Is.EqualTo(-76));
            Assert.That(session.finishZ,Is.EqualTo(76));
            Assert.That(session.font,Is.Not.Null);
        }
        [Test] public void VisibleMeshesHaveSupportedMaterials()
        {
            var renderers=All<Renderer>();Assert.That(renderers.Length,Is.GreaterThan(500));
            foreach(var renderer in renderers)
                foreach(var material in renderer.sharedMaterials)
                {Assert.That(material,Is.Not.Null,renderer.name);Assert.That(material.shader.name,Does.Not.Contain("InternalError"),renderer.name);}
        }
    }
}
