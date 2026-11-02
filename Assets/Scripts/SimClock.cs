using UnityEngine;

namespace SpectreEX
{
    // The one clock the game runs on, so the frame rate never changes its speed or its results. The
    // player moves in fixed sub-steps; every SubSteps of them is one of the original's 18.2 Hz ticks,
    // on which the robots, shots and level timers advance, in that order. Each frame runs however
    // many steps have come due and draws the moment between the last two. Unity's maximumDeltaTime
    // limits the catching up after a long stall.
    public class SimClock : MonoBehaviour
    {
        public const int SubSteps = PlayerMotor.SubSteps;
        const double StepSeconds = 1.0 / (PlayerMotor.TickRate * SubSteps);

        GameManager gm;
        RobotDirector director;
        double accumulator;
        int subStep;

        void Awake()
        {
            gm = GetComponent<GameManager>();
            director = GetComponent<RobotDirector>();
        }

        void Update()
        {
            var motor = gm.player != null ? gm.player.Motor : null;
            if (motor != null) motor.BeforeSim();

            accumulator += Time.deltaTime;
            while (accumulator >= StepSeconds)
            {
                accumulator -= StepSeconds;
                if (subStep == 0 && director != null) director.FireWeapons();   // _DO_PLAYER_MOVE fires before the tick's move
                bool tickEnds = ++subStep == SubSteps;
                if (tickEnds) subStep = 0;
                if (motor != null && gm.Playing) motor.SubStep(tickEnds);
                if (tickEnds)
                {
                    if (director != null) director.SimTick();
                    gm.SimTick();
                }
            }

            float alpha = (float)(accumulator / StepSeconds);
            if (motor != null) motor.Present(alpha);
            if (director != null) director.Present((subStep + alpha) / SubSteps);
        }
    }
}
