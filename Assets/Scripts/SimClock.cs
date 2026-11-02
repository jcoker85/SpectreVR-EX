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

        // Original ticks since the game started, and how far the frame is drawn towards the next one
        public static long Ticks { get; private set; }
        public static float TickFraction { get; private set; }

        GameManager gm;
        RobotDirector director;
        double accumulator;
        int subStep;
        static bool dropFrame;

        // _START_LEVEL calls _CHECK_TIMER once the level is loaded, so the time spent loading never
        // reaches the game. Here the frame that built the level, and drew it for the first time, is dropped.
        public static void DropFrame() => dropFrame = true;

        void Awake()
        {
            gm = GetComponent<GameManager>();
            director = GetComponent<RobotDirector>();
        }

        void Update()
        {
            var motor = gm.player != null ? gm.player.Motor : null;
            if (motor != null) motor.BeforeSim();

            if (dropFrame) dropFrame = false;
            else accumulator += Time.deltaTime;
            while (accumulator >= StepSeconds)
            {
                accumulator -= StepSeconds;
                if (subStep == 0 && director != null) director.FireWeapons();   // _DO_PLAYER_MOVE fires before the tick's move
                bool tickEnds = ++subStep == SubSteps;
                if (tickEnds) { subStep = 0; Ticks++; }
                if (motor != null && gm.Playing) motor.SubStep(tickEnds);
                if (tickEnds)
                {
                    if (director != null) director.SimTick();
                    gm.SimTick();
                }
            }

            float alpha = (float)(accumulator / StepSeconds);
            if (motor != null) motor.Present(alpha);
            TickFraction = (subStep + alpha) / SubSteps;
            if (director != null) director.Present(TickFraction);
        }
    }
}
