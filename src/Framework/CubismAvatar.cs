using System;
using System.Collections.Generic;
using System.IO;
using PartnerLive2D.Core;

namespace PartnerLive2D.Framework
{
    // Facade over the ported Cubism Native Framework: loads a model3.json with everything it references
    // and runs the per-frame update in the order of the SDK sample's LAppModel::Update.
    public sealed class CubismAvatar : IDisposable
    {
        public const int PriorityNone = 0;
        public const int PriorityIdle = 1;
        public const int PriorityNormal = 2;
        public const int PriorityForce = 3;

        readonly Model3Json _settings;
        readonly CubismModel _model;
        readonly Random _random = new Random();

        readonly MotionManager _motionManager = new MotionManager();
        readonly ExpressionMotionManager _expressionManager = new ExpressionMotionManager();
        readonly CubismTargetPoint _dragManager = new CubismTargetPoint();
        readonly CubismBreath _breath = new CubismBreath();
        readonly CubismLook _look = new CubismLook();
        CubismEyeBlink _eyeBlink;
        CubismPhysics _physics;
        CubismPose _pose;

        readonly Dictionary<string, List<CubismMotion>> _motions = new Dictionary<string, List<CubismMotion>>();
        readonly Dictionary<string, CubismExpressionMotion> _expressions = new Dictionary<string, CubismExpressionMotion>();
        readonly List<string> _expressionNames = new List<string>();
        bool _expressionStarted;

        readonly string[] _texturePaths;
        readonly string[] _motionGroups;
        readonly string[] _hitAreaNames;
        bool _motionUpdated;
        float _modelOpacity = 1f;

        public CubismModel Model { get { return _model; } }
        public string[] TexturePaths { get { return _texturePaths; } }
        public string[] MotionGroups { get { return _motionGroups; } }
        public string[] ExpressionNames { get { return _expressionNames.ToArray(); } }
        public string[] HitAreaNames { get { return _hitAreaNames; } }

        // group that is auto-played at priority 1 whenever no motion is running
        public string IdleGroup { get; set; }

        // Opacity set by a motion "Opacity" model curve (1 when no motion uses one).
        public float ModelOpacity { get { return _modelOpacity; } }

        public bool IsMotionFinished { get { return _motionManager.IsFinished(); } }

        CubismAvatar(Model3Json settings)
        {
            _settings = settings;
            IdleGroup = "Idle";

            _model = settings.MocFile.EndsWith(".rig3.json", StringComparison.OrdinalIgnoreCase)
                ? (CubismModel)new RigModel(File.ReadAllText(settings.Resolve(settings.MocFile), System.Text.Encoding.UTF8))
                : new MocModel(File.ReadAllBytes(settings.Resolve(settings.MocFile)));
            try
            {
                _texturePaths = new string[settings.TextureFiles.Count];
                for (int i = 0; i < _texturePaths.Length; i++)
                {
                    string f = settings.TextureFiles[i];
                    _texturePaths[i] = string.IsNullOrEmpty(f) ? "" : settings.Resolve(f);
                }

                _motionGroups = settings.MotionGroupNames.ToArray();

                _hitAreaNames = new string[settings.HitAreas.Count];
                for (int i = 0; i < _hitAreaNames.Length; i++) _hitAreaNames[i] = settings.HitAreas[i].Name;

                Setup();
            }
            catch
            {
                _model.Dispose();
                throw;
            }
        }

        public static CubismAvatar Load(string model3JsonPath)
        {
            return new CubismAvatar(Model3Json.Load(model3JsonPath));
        }

        void Setup()
        {
            // expressions
            foreach (ExpressionEntry e in _settings.Expressions)
            {
                if (string.IsNullOrEmpty(e.Name) || string.IsNullOrEmpty(e.File)) continue;
                CubismExpressionMotion exp = CubismExpressionMotion.Load(_settings.Resolve(e.File));
                if (!_expressions.ContainsKey(e.Name)) _expressionNames.Add(e.Name);
                _expressions[e.Name] = exp;
            }

            // physics / pose
            if (_settings.PhysicsFile != null)
                _physics = CubismPhysics.Parse(Json.ParseFile(_settings.Resolve(_settings.PhysicsFile)));
            if (_settings.PoseFile != null)
                _pose = CubismPose.Parse(Json.ParseFile(_settings.Resolve(_settings.PoseFile)));

            // eye blink (only when the model declares EyeBlink parameters)
            if (_settings.EyeBlinkIds.Count > 0)
                _eyeBlink = new CubismEyeBlink(_settings.EyeBlinkIds, _random);

            // breath: standard LAppModel parameters
            _breath.SetParameters(new[]
            {
                new CubismBreath.BreathParameterData("ParamAngleX", 0.0f, 15.0f, 6.5345f, 0.5f),
                new CubismBreath.BreathParameterData("ParamAngleY", 0.0f, 8.0f, 3.5345f, 0.5f),
                new CubismBreath.BreathParameterData("ParamAngleZ", 0.0f, 10.0f, 5.5345f, 0.5f),
                new CubismBreath.BreathParameterData("ParamBodyAngleX", 0.0f, 4.0f, 15.5345f, 0.5f),
                new CubismBreath.BreathParameterData("ParamBreath", 0.5f, 0.5f, 3.2345f, 0.5f),
            });

            // look: drag factors of LAppModel
            _look.SetParameters(new[]
            {
                new CubismLook.LookParameterData("ParamAngleX", 30.0f, 0.0f, 0.0f),
                new CubismLook.LookParameterData("ParamAngleY", 0.0f, 30.0f, 0.0f),
                new CubismLook.LookParameterData("ParamAngleZ", 0.0f, 0.0f, -30.0f),
                new CubismLook.LookParameterData("ParamBodyAngleX", 10.0f, 0.0f, 0.0f),
                new CubismLook.LookParameterData("ParamEyeBallX", 1.0f, 0.0f, 0.0f),
                new CubismLook.LookParameterData("ParamEyeBallY", 0.0f, 1.0f, 0.0f),
                new CubismLook.LookParameterData("ParamCamX", 1.0f, 0.0f, 0.0f),
                new CubismLook.LookParameterData("ParamCamY", 0.0f, 1.0f, 0.0f),
            });

            // motions (preloaded like PreloadMotionGroup)
            foreach (string group in _settings.MotionGroupNames)
            {
                var list = new List<CubismMotion>();
                foreach (MotionEntry me in _settings.Motions[group])
                {
                    CubismMotion m = CubismMotion.Load(_settings.Resolve(me.File));
                    if (me.FadeInTime >= 0.0f) m.FadeInSeconds = me.FadeInTime;
                    if (me.FadeOutTime >= 0.0f) m.FadeOutSeconds = me.FadeOutTime;
                    m.SetEffectIds(_settings.EyeBlinkIds, _settings.LipSyncIds);
                    m.OnModelOpacity = OnModelOpacity;
                    list.Add(m);
                }
                _motions[group] = list;
            }

            _model.SaveParameters();
        }

        void OnModelOpacity(float v) { _modelOpacity = v; }

        // ---- motions -----------------------------------------------------------------

        public int GetMotionCount(string group)
        {
            List<CubismMotion> list;
            return group != null && _motions.TryGetValue(group, out list) ? list.Count : 0;
        }

        public bool StartMotion(string group, int index, int priority)
        {
            List<CubismMotion> list;
            if (group == null || !_motions.TryGetValue(group, out list)) return false;
            if (index < 0 || index >= list.Count) return false;

            if (priority == PriorityForce)
                _motionManager.ReservePriority = priority;
            else if (!_motionManager.ReserveMotion(priority))
                return false;

            _motionManager.StartMotionPriority(list[index], priority);
            return true;
        }

        public bool StartRandomMotion(string group, int priority)
        {
            int count = GetMotionCount(group);
            if (count == 0) return false;
            return StartMotion(group, _random.Next(count), priority);
        }

        // Return to idle after a bounded UI reaction; does not reset expressions or gaze.
        public void StopMotions() { _motionManager.StopAllMotions(); }

        // ---- expressions -------------------------------------------------------------

        // null clears the active expression (fades back to neutral); unknown names are ignored
        public void SetExpression(string name)
        {
            if (name == null)
            {
                if (_expressionStarted)
                    _expressionManager.StartMotion(CubismExpressionMotion.CreateEmpty(0.5f));
                return;
            }
            CubismExpressionMotion exp;
            if (!_expressions.TryGetValue(name, out exp)) return;
            _expressionManager.StartMotion(exp);
            _expressionStarted = true;
        }

        // ---- look / hit --------------------------------------------------------------

        public void SetLookTarget(float x, float y)
        {
            _dragManager.Set(x, y);
        }

        public int GetHitAreaDrawableIndex(string hitAreaName)
        {
            if (hitAreaName == null) return -1;
            foreach (HitAreaEntry h in _settings.HitAreas)
            {
                if (h.Name == hitAreaName)
                    return _model.GetDrawableIndex(h.Id);
            }
            return -1;
        }

        // Settles the physics pendulums to the current parameter values (optional, call after loading).
        public void StabilizePhysics()
        {
            if (_physics != null) _physics.Stabilization(_model);
        }

        // ---- frame update ------------------------------------------------------------

        // Same order as LAppModel::Update (Cubism SDK for Native 5-r.5).
        public void Tick(float deltaSeconds)
        {
            _motionUpdated = false;

            _model.LoadParameters();
            if (_motionManager.IsFinished())
            {
                // no motion running: pick a random idle motion
                StartRandomMotion(IdleGroup, PriorityIdle);
            }
            else
            {
                _motionUpdated = _motionManager.UpdateMotion(_model, deltaSeconds);
            }
            _model.SaveParameters();

            // updaters in scheduler order: EyeBlink 200, Expression 300, Look 400, Breath 500, Physics 600, Pose 700
            if (_eyeBlink != null && !_motionUpdated)
                _eyeBlink.UpdateParameters(_model, deltaSeconds);

            if (_expressions.Count > 0)
                _expressionManager.UpdateMotion(_model, deltaSeconds);

            _dragManager.Update(deltaSeconds);
            _look.UpdateParameters(_model, _dragManager.X, _dragManager.Y);

            _breath.UpdateParameters(_model, deltaSeconds);

            if (_physics != null)
                _physics.Evaluate(_model, deltaSeconds);

            if (_pose != null)
                _pose.UpdateParameters(_model, deltaSeconds);

            _model.Update();
        }

        public void Dispose()
        {
            _model.Dispose();
        }
    }
}
