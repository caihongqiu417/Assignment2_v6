    using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class RollerAgent : Agent
{
    public enum HeuristicMode
    {
        PlayerInput = 0,
        ConservativeCenter = 1,
        OrbitLimitCycle = 2,
        OscillatingAxes = 3
    }

    private Rigidbody rBody;

    [Header("References")]
    public RollerAgent Opponent;
    public RollerGameController GameController;

    [Header("Movement")]
    public float forceMultiplier = 10f;

    [Header("Heuristic Input")]
    public string playerInput;

    [Header("Heuristic Strategy")]
    public HeuristicMode heuristicMode = HeuristicMode.PlayerInput;

    [Header("Conservative Heuristic Tuning")]
    [Tooltip("Center dead zone for conservative mode. Agent prioritizes braking when close enough to center.")]
    public float conservativeCenterDeadZone = 0.15f;

    [Tooltip("How strongly position error pulls the agent toward center.")]
    public float conservativePositionGain = 1.2f;

    [Tooltip("How strongly current velocity is damped (acts like active braking).")]
    public float conservativeVelocityGain = 1.0f;

    [Tooltip("Extra inward pull that increases with distance from center.")]
    public float conservativeEdgePullGain = 1.1f;

    [Tooltip("Additional braking when moving away from center.")]
    public float conservativeOutwardBrakeGain = 1.5f;

    [Tooltip("Outward speed above this value triggers extra outward-brake force.")]
    public float conservativeOutwardBrakeThreshold = 0.15f;

    [Tooltip("Threshold used to convert heuristic continuous control into discrete actions.")]
    public float heuristicAxisThreshold = 0.1f;

    private Vector2 orbitVelocityErrorIntegral = Vector2.zero;
    private Vector2 orbitVelocityErrorPrev = Vector2.zero;
    private int heuristicDecisionPeriod = 1;

    void Start()
    {
        rBody = GetComponent<Rigidbody>();
    }

    public Rigidbody Body => rBody;

    public override void Initialize()
    {
        if (rBody == null)
        {
            rBody = GetComponent<Rigidbody>();
        }

        var decisionRequester = GetComponent<DecisionRequester>();
        heuristicDecisionPeriod = decisionRequester != null ? Mathf.Max(1, decisionRequester.DecisionPeriod) : 1;
    }

    public override void OnEpisodeBegin()
    {
        orbitVelocityErrorIntegral = Vector2.zero;
        orbitVelocityErrorPrev = Vector2.zero;
        GameController.TryResetEpisode();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        float arenaHalfSize = GameController.arenaHalfSize;
        arenaHalfSize = Mathf.Max(0.001f, arenaHalfSize);

        // Self position on x-z plane (2, normalized)
        sensor.AddObservation(transform.localPosition.x / arenaHalfSize);
        sensor.AddObservation(transform.localPosition.z / arenaHalfSize);

        // Opponent position on x-z plane (2, normalized)
        sensor.AddObservation(Opponent.transform.localPosition.x / arenaHalfSize);
        sensor.AddObservation(Opponent.transform.localPosition.z / arenaHalfSize);

        // Self velocity (2)
        sensor.AddObservation(rBody.velocity.x);
        sensor.AddObservation(rBody.velocity.z);

        // Opponent velocity (2)
        sensor.AddObservation(Opponent.Body.velocity.x);
        sensor.AddObservation(Opponent.Body.velocity.z);

        // Self angular velocity (2)
        sensor.AddObservation(rBody.angularVelocity.x);
        sensor.AddObservation(rBody.angularVelocity.z);

        // Opponent angular velocity (2)
        sensor.AddObservation(Opponent.Body.angularVelocity.x);
        sensor.AddObservation(Opponent.Body.angularVelocity.z);
    }

    public override void OnActionReceived(ActionBuffers actionBuffers)
    {
        // Two discrete branches:
        // 0 = no move, 1 = negative axis, 2 = positive axis
        var discreteActions = actionBuffers.DiscreteActions;
        Vector3 controlSignal = Vector3.zero;
        controlSignal.x = DiscreteToAxis(discreteActions[0]);
        controlSignal.z = DiscreteToAxis(discreteActions[1]);
        rBody.AddForce(controlSignal * forceMultiplier);

        // Small step penalty to encourage faster wins.
        AddReward(-0.001f);
        GameController.ResolveRoundIfNeeded();
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActionsOut = actionsOut.DiscreteActions;
        float horizontal = 0f;
        float vertical = 0f;

        if (heuristicMode != HeuristicMode.OrbitLimitCycle)
        {
            orbitVelocityErrorIntegral = Vector2.zero;
            orbitVelocityErrorPrev = Vector2.zero;
        }

        switch (heuristicMode)
        {
            case HeuristicMode.ConservativeCenter:
                // Position + velocity controller on XZ plane:
                // - Pull toward center
                // - Dampen current velocity
                // - Add stronger inward pull near edge
                // - Brake extra when currently moving away from center
                Vector3 localPos3 = transform.localPosition;
                Vector2 pos = new Vector2(localPos3.x, localPos3.z);
                Vector2 vel = new Vector2(rBody.velocity.x, rBody.velocity.z);
                float distance = pos.magnitude;
                float arenaHalfSize = GameController != null ? GameController.arenaHalfSize : 1f;
                arenaHalfSize = Mathf.Max(0.001f, arenaHalfSize);

                Vector2 control = (-pos * conservativePositionGain) - (vel * conservativeVelocityGain);

                if (distance > 1e-4f)
                {
                    Vector2 inwardDir = -pos / distance;
                    float edgeFactor = Mathf.Clamp01(distance / arenaHalfSize);
                    control += inwardDir * (conservativeEdgePullGain * edgeFactor);

                    float outwardSpeed = Vector2.Dot(vel, pos / distance);
                    if (outwardSpeed > conservativeOutwardBrakeThreshold)
                    {
                        control += inwardDir * ((outwardSpeed - conservativeOutwardBrakeThreshold) * conservativeOutwardBrakeGain);
                    }
                }

                if (distance <= conservativeCenterDeadZone)
                {
                    // Close to center: prioritize stopping residual drift.
                    control = -vel * (conservativeVelocityGain * 1.5f);
                }

                horizontal = control.x;
                vertical = control.y;
                break;
            case HeuristicMode.OrbitLimitCycle:
                // Limit cycle controller (parameters hardcoded)
                const float orbitRadiusRatio = 0.55f;
                const float orbitTangentialSpeed = 4.2f;
                const float orbitRadialGain = 2.8f;
                const float orbitVelocityKp = 3.2f;
                const float orbitVelocityKi = 0.45f;
                const float orbitVelocityKd = 0.08f;
                const float orbitControlGain = 2.2f;
                const float orbitLowSpeedBoostGain = 2.4f;
                const float orbitLowSpeedBoostThreshold = 3.2f;
                const float orbitIntegralClamp = 1.5f;
                const float orbitSafetyEdgeRatio = 0.86f;
                const float orbitSafetyBrakeGain = 4.2f;
                const float orbitSafetyOutwardSpeedThreshold = 0.35f;

                Vector3 orbitLocalPos3 = transform.localPosition;
                Vector2 orbitPos = new Vector2(orbitLocalPos3.x, orbitLocalPos3.z);
                Vector2 orbitVel = new Vector2(rBody.velocity.x, rBody.velocity.z);
                float orbitDistance = orbitPos.magnitude;
                float orbitArenaHalfSize = GameController != null ? GameController.arenaHalfSize : 1f;
                orbitArenaHalfSize = Mathf.Max(0.001f, orbitArenaHalfSize);
                float targetRadius = Mathf.Clamp01(orbitRadiusRatio) * orbitArenaHalfSize;

                Vector2 radialDir;
                if (orbitDistance > 1e-4f)
                {
                    radialDir = orbitPos / orbitDistance;
                }
                else
                {
                    radialDir = Vector2.right;
                }

                Vector2 tangentDir = new Vector2(-radialDir.y, radialDir.x);

                float radialError = targetRadius - orbitDistance;
                Vector2 desiredVel = tangentDir * orbitTangentialSpeed + radialDir * (radialError * orbitRadialGain);
                Vector2 velocityError = desiredVel - orbitVel;

                float dt = Mathf.Max(Time.fixedDeltaTime, 1e-3f);
                orbitVelocityErrorIntegral += velocityError * dt;
                orbitVelocityErrorIntegral = Vector2.ClampMagnitude(orbitVelocityErrorIntegral, Mathf.Max(0.01f, orbitIntegralClamp));
                Vector2 velocityErrorDerivative = (velocityError - orbitVelocityErrorPrev) / dt;
                orbitVelocityErrorPrev = velocityError;

                Vector2 orbitControl =
                    velocityError * orbitVelocityKp +
                    orbitVelocityErrorIntegral * orbitVelocityKi +
                    velocityErrorDerivative * orbitVelocityKd;

                float speedNow = orbitVel.magnitude;
                if (speedNow < orbitLowSpeedBoostThreshold && desiredVel.sqrMagnitude > 1e-6f)
                {
                    float speedGap = orbitLowSpeedBoostThreshold - speedNow;
                    orbitControl += desiredVel.normalized * (speedGap * orbitLowSpeedBoostGain);
                }

                float edgeRatio = orbitDistance / orbitArenaHalfSize;
                if (edgeRatio > orbitSafetyEdgeRatio)
                {
                    float outwardSpeed = Vector2.Dot(orbitVel, radialDir);
                    float edgeFactor = Mathf.InverseLerp(orbitSafetyEdgeRatio, 1f, Mathf.Clamp01(edgeRatio));
                    float safetyBrake = orbitSafetyBrakeGain * (1f + edgeFactor);
                    orbitControl += (-radialDir) * safetyBrake;

                    if (outwardSpeed > orbitSafetyOutwardSpeedThreshold)
                    {
                        orbitControl += (-radialDir) * ((outwardSpeed - orbitSafetyOutwardSpeedThreshold) * orbitSafetyBrakeGain);
                    }
                }

                orbitControl *= Mathf.Max(0.1f, orbitControlGain);
                horizontal = orbitControl.x;
                vertical = orbitControl.y;
                break;
            case HeuristicMode.OscillatingAxes:
                // Per-frame lookahead safety policy:
                // prefer accelerate-along-velocity, then noop, then brake.
                const float oscBoundRatio = 0.8f;
                const float axisStillSpeedEpsilon = 0.00005f;
                const float axisCenterPosEpsilon = 0.02f;

                float policyArenaHalfSize = GameController != null ? GameController.arenaHalfSize : 1f;
                policyArenaHalfSize = Mathf.Max(0.001f, policyArenaHalfSize);
                Vector3 policyPos3 = transform.localPosition;
                Vector2 policyPos = new Vector2(policyPos3.x, policyPos3.z);
                Vector2 policyVel = new Vector2(rBody.velocity.x, rBody.velocity.z);
                float dtLookahead = Mathf.Max(1e-3f, Time.fixedDeltaTime);
                float axisAcceleration = forceMultiplier / Mathf.Max(0.001f, rBody.mass);
                float linearDrag = 0f;
                int holdStepsBeforeBrake = Mathf.Max(1, heuristicDecisionPeriod);

                float effectiveAxisBound = policyArenaHalfSize * oscBoundRatio;

                int actionX = SelectAxisActionByLookahead(
                    policyPos.x,
                    policyVel.x,
                    effectiveAxisBound,
                    axisAcceleration,
                    dtLookahead,
                    holdStepsBeforeBrake,
                    linearDrag,
                    axisStillSpeedEpsilon,
                    axisCenterPosEpsilon);

                int actionZ = SelectAxisActionByLookahead(
                    policyPos.y,
                    policyVel.y,
                    effectiveAxisBound,
                    axisAcceleration,
                    dtLookahead,
                    holdStepsBeforeBrake,
                    linearDrag,
                    axisStillSpeedEpsilon,
                    axisCenterPosEpsilon);

                horizontal = actionX;
                vertical = actionZ;
                break;
            case HeuristicMode.PlayerInput:
            default:
                string inputPrefix = playerInput.ToString();
                horizontal = Input.GetAxisRaw($"{inputPrefix}_Horizontal");
                vertical = Input.GetAxisRaw($"{inputPrefix}_Vertical");
                break;
        }

        discreteActionsOut[0] = AxisToDiscrete(horizontal, heuristicAxisThreshold);
        discreteActionsOut[1] = AxisToDiscrete(vertical, heuristicAxisThreshold);
    }

    private static int SelectAxisActionByLookahead(
        float position,
        float velocity,
        float bound,
        float axisAcceleration,
        float dt,
        int holdStepsBeforeBrake,
        float linearDrag,
        float stillSpeedEpsilon,
        float centerPosEpsilon)
    {
        // Hard constraint: once at/over boundary, never try outward probing.
        if (Mathf.Abs(position) >= bound)
        {
            int boundaryInwardDir = 0;
            if (position > 0f) boundaryInwardDir = -1;
            else if (position < 0f) boundaryInwardDir = 1;
            return boundaryInwardDir;
        }

        int speedDir = 0;
        if (velocity > 0f) speedDir = 1;
        else if (velocity < 0f) speedDir = -1;

        if (speedDir != 0)
        {
            if (WillRemainSafeAfterEmergencyBrake(position, velocity, speedDir, bound, axisAcceleration, dt, holdStepsBeforeBrake, linearDrag)) return speedDir;
            if (WillRemainSafeAfterEmergencyBrake(position, velocity, 0, bound, axisAcceleration, dt, holdStepsBeforeBrake, linearDrag)) return 0;
            return -speedDir;
        }

        // Near-zero speed: bias toward center to avoid edge probing.
        int inwardDir = 0;
        if (position > centerPosEpsilon) inwardDir = -1;
        else if (position < -centerPosEpsilon) inwardDir = 1;
        else inwardDir = 0;

        if (inwardDir == 0) return 0;
        if (WillRemainSafeAfterEmergencyBrake(position, velocity, inwardDir, bound, axisAcceleration, dt, holdStepsBeforeBrake, linearDrag)) return inwardDir;
        return 0;
    }

    private static bool WillRemainSafeAfterEmergencyBrake(
        float position,
        float velocity,
        int action,
        float bound,
        float axisAcceleration,
        float dt,
        int holdStepsBeforeBrake,
        float linearDrag)
    {
        float effectiveBound = bound;
        int clampedAction = Mathf.Clamp(action, -1, 1);

        // Phase 1: closed-form state after holding candidate action until next decision.
        AdvanceAxisClosedForm(position, velocity, clampedAction, axisAcceleration, linearDrag, dt, holdStepsBeforeBrake, out float posAfterHold, out float velAfterHold);
        if (Mathf.Abs(posAfterHold) >= effectiveBound) return false;

        // Phase 2: closed-form estimate of braking extremum until velocity reaches zero.
        float stopPos = ComputeBrakeToZeroPositionClosedForm(
            posAfterHold,
            velAfterHold,
            axisAcceleration,
            linearDrag,
            dt);

        return Mathf.Abs(stopPos) < effectiveBound;
    }

    private static float ComputeBrakeToZeroPositionClosedForm(
        float position,
        float velocity,
        float axisAcceleration,
        float linearDrag,
        float dt)
    {
        if (Mathf.Abs(velocity) <= 1e-6f) return position;
        if (axisAcceleration <= 1e-6f) return Mathf.Sign(velocity) * float.MaxValue;

        int brakeAction = velocity > 0f ? -1 : 1;
        float alpha = Mathf.Clamp01(1f - linearDrag * dt);
        float c = brakeAction * axisAcceleration * dt;
        int nToZero = EstimateStepsToVelocitySignFlip(alpha, c, velocity);
        if (nToZero <= 0) return position;
        if (nToZero >= int.MaxValue / 8) return Mathf.Sign(velocity) * float.MaxValue;

        // Position extremum occurs around the velocity zero-crossing.
        AdvanceAxisClosedForm(position, velocity, brakeAction, axisAcceleration, linearDrag, dt, Mathf.Max(0, nToZero - 1), out float posPrev, out _);
        AdvanceAxisClosedForm(position, velocity, brakeAction, axisAcceleration, linearDrag, dt, nToZero, out float posNow, out _);
        return Mathf.Abs(posPrev) > Mathf.Abs(posNow) ? posPrev : posNow;
    }

    private static int EstimateStepsToVelocitySignFlip(float alpha, float c, float velocity0)
    {
        if (Mathf.Abs(velocity0) <= 1e-6f) return 0;
        float s = Mathf.Sign(velocity0);

        if (Mathf.Abs(1f - alpha) <= 1e-6f)
        {
            float signedDeltaPerStep = s * c;
            if (signedDeltaPerStep >= -1e-6f) return int.MaxValue / 4;
            return Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(velocity0) / (-signedDeltaPerStep)));
        }

        // y_n = s*v_n = alpha^n*y0 + cSigned*(1-alpha^n)/(1-alpha), solve y_n <= 0.
        float y0 = Mathf.Abs(velocity0);
        float cSigned = s * c;
        float oneMinusAlpha = 1f - alpha;
        if (cSigned >= -1e-6f) return int.MaxValue / 4;
        float fixedPoint = cSigned / oneMinusAlpha; // negative when braking is effective
        float denominator = y0 - fixedPoint;
        if (denominator <= 1e-6f) return 1;

        // alpha^n <= (-fixedPoint)/(y0-fixedPoint)
        float ratio = (-fixedPoint) / denominator;
        ratio = Mathf.Clamp(ratio, 1e-6f, 1f);

        float nReal = Mathf.Log(ratio) / Mathf.Log(alpha);
        int n = Mathf.CeilToInt(nReal);
        return Mathf.Max(1, n);
    }

    private static void AdvanceAxisClosedForm(
        float position,
        float velocity,
        int action,
        float axisAcceleration,
        float linearDrag,
        float dt,
        int steps,
        out float positionOut,
        out float velocityOut)
    {
        if (steps <= 0)
        {
            positionOut = position;
            velocityOut = velocity;
            return;
        }

        float alpha = Mathf.Clamp01(1f - linearDrag * dt);
        float c = Mathf.Clamp(action, -1, 1) * axisAcceleration * dt;

        if (Mathf.Abs(1f - alpha) <= 1e-6f)
        {
            // Near zero drag: v_{k+1} = v_k + c
            velocityOut = velocity + steps * c;
            float sumVNoDrag = steps * velocity + 0.5f * c * steps * (steps + 1);
            positionOut = position + dt * sumVNoDrag;
            return;
        }

        float pow = Mathf.Pow(alpha, steps);
        float oneMinusAlpha = 1f - alpha;
        float geomSum = alpha * (1f - pow) / oneMinusAlpha; // sum_{i=1..n} alpha^i
        float velocityBias = c / oneMinusAlpha;

        velocityOut = pow * velocity + velocityBias * (1f - pow);
        float sumV = velocity * geomSum + velocityBias * (steps - geomSum);
        positionOut = position + dt * sumV;
    }

    private static int AxisToDiscrete(float axis, float threshold = 0.5f)
    {
        threshold = Mathf.Clamp(threshold, 0.01f, 1f);
        if (axis < -threshold) return 1;
        if (axis > threshold) return 2;
        return 0;
    }

    private static float DiscreteToAxis(int action)
    {
        switch (action)
        {
            case 1:
                return -1f;
            case 2:
                return 1f;
            default:
                return 0f;
        }
    }
}
