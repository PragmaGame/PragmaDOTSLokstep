using Unity.Entities;
using UnityEngine;
#if LOCKSTEP_EXAMPLES_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Pragma.Lockstep.Examples
{
    /// <summary>
    /// Reads the local devices once per frame and writes the lockstep input. Runs in client and offline worlds only;
    /// the simulation never touches devices.
    /// </summary>
    [UpdateInGroup(typeof(LockstepInputSystemGroup))]
    public partial class ArenaInputSystem : SystemBase
    {
        private int _colorIndex;

        protected override void OnCreate()
        {
            RequireForUpdate<LockstepLocalInput>();
        }

        protected override void OnUpdate()
        {
            var input = new ArenaInput();
            var changeColor = false;
            var move = Vector2.zero;

#if LOCKSTEP_EXAMPLES_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
                {
                    move.x -= 1f;
                }
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
                {
                    move.x += 1f;
                }
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
                {
                    move.y -= 1f;
                }
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
                {
                    move.y += 1f;
                }
                if (keyboard.spaceKey.isPressed)
                {
                    input.buttons |= ArenaInput.FIRE_BUTTON;
                }
                if (keyboard.leftShiftKey.isPressed)
                {
                    input.buttons |= ArenaInput.DASH_BUTTON;
                }
                changeColor |= keyboard.cKey.wasPressedThisFrame;
            }

            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                var stick = gamepad.leftStick.ReadValue();
                if (stick.sqrMagnitude > move.sqrMagnitude)
                {
                    move = stick;
                }
                if (gamepad.buttonSouth.isPressed)
                {
                    input.buttons |= ArenaInput.FIRE_BUTTON;
                }
                if (gamepad.buttonEast.isPressed)
                {
                    input.buttons |= ArenaInput.DASH_BUTTON;
                }
                changeColor |= gamepad.buttonNorth.wasPressedThisFrame;
            }
#endif

            // Quantize here: these bytes, not the floats, are what every client simulates.
            move = Vector2.ClampMagnitude(move, 1f);
            input.moveX = (sbyte)Mathf.RoundToInt(move.x * 100f);
            input.moveY = (sbyte)Mathf.RoundToInt(move.y * 100f);

            var localInput = SystemAPI.GetSingleton<LockstepLocalInput>();
            localInput.Set(input);
            SystemAPI.SetSingleton(localInput);

            if (changeColor)
            {
                _colorIndex++;
                SystemAPI.GetSingletonBuffer<LockstepCommand>().Add(LockstepCommand.Create(new ArenaChangeColorCommand { colorIndex = _colorIndex }));
            }
        }
    }
}
