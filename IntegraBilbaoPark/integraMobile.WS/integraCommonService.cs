﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Web;
using System.Globalization;
using System.Configuration;
using System.Threading;
using integraMobile.Domain;
using integraMobile.Domain.Abstract;
using integraMobile.Infrastructure;
using integraMobile.Infrastructure.Logging.Tools;
using integraMobile.ExternalWS;
using integraMobile.WS.Resources;

namespace integraMobile.WS
{
    public class integraCommonService
    {
        //Log4net Wrapper class
        private static readonly CLogWrapper m_Log = new CLogWrapper(typeof(integraCommonService));

        private ICustomersRepository customersRepository;
        private IInfraestructureRepository infraestructureRepository;
        private IGeograficAndTariffsRepository geograficAndTariffsRepository;

        public integraCommonService(ICustomersRepository oCustomersRepository, IInfraestructureRepository oInfraestructureRepository, IGeograficAndTariffsRepository oGeograficAndTariffsRepository)
        {
            this.customersRepository = oCustomersRepository;
            this.infraestructureRepository = oInfraestructureRepository;
            this.geograficAndTariffsRepository = oGeograficAndTariffsRepository;
        }

        #region Public Methods

        public ResultType ChargeOffstreetOperation(OffstreetOperationType operationType, string strPlate, double dChangeToApply, int iQuantity, int iTime,
                                                    DateTime dtEntryDate, DateTime dtNotifyEntryDate, DateTime? dtPaymentDate, DateTime? dtEndDate, DateTime? dtExitLimitDate, GROUPS_OFFSTREET_WS_CONFIGURATION oParkingconfiguration, GROUP oGroup, string sLogicalId, string sTariff, string sGate, string sSpaceDesc, bool bMustNotify,
                                                    ref USER oUser, int iOSType, decimal? dMobileSessionId, decimal? dLatitude, decimal? dLongitude, string strAppVersion,
                                                    decimal dPercVAT1, decimal dPercVAT2, decimal dPercFEE, int iPercFEETopped, int iFixedFEE, 
                                                    int iPartialVAT1, int iPartialPercFEE, int iPartialFixedFEE, int iTotalQuantity,
                                                    string sDiscountCodes,
                                                    ref SortedList parametersOut, out int iCurrencyChargedQuantity, out decimal dOperationID,
                                                    out decimal? dRechargeId, out int? iBalanceAfterRecharge, out bool bRestoreBalanceInCaseOfRefund)
        {
            ResultType rtRes = ResultType.Result_OK;
            iCurrencyChargedQuantity = 0;
            double dChangeFee = 0;
            decimal dBalanceCurID = oUser.CURRENCy.CUR_ID;
            dOperationID = -1;
            dRechargeId = null;
            bRestoreBalanceInCaseOfRefund = true;
            PaymentSuscryptionType suscriptionType = PaymentSuscryptionType.pstPrepay;
            iBalanceAfterRecharge = null;

            try
            {
                if (iTotalQuantity != 0)
                {
                    parametersOut["autorecharged"] = "0";
                    iCurrencyChargedQuantity = ChangeQuantityFromInstallationCurToUserCur(iTotalQuantity, dChangeToApply, oGroup.INSTALLATION, oUser, out dChangeFee);

                    if (iCurrencyChargedQuantity < 0)
                    {
                        rtRes = (ResultType)iCurrencyChargedQuantity;
                        Logger_AddLogMessage(string.Format("ChargeOffstreetOperation::Error Changing quantity {0} ", rtRes.ToString()), LogLevels.logERROR);
                        return rtRes;
                    }



                    if ((oUser.USR_BALANCE > 0) ||
                        (oUser.USR_SUSCRIPTION_TYPE == (int)PaymentSuscryptionType.pstPrepay))
                    {
                        int iNewBalance = oUser.USR_BALANCE - iCurrencyChargedQuantity;


                        if (iNewBalance < 0)
                        {

                            if ((oUser.CUSTOMER_PAYMENT_MEAN != null) &&
                                (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_ENABLED == 1) &&
                                (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_VALID == 1))
                            {


                                if ((oUser.USR_SUSCRIPTION_TYPE == (int)PaymentSuscryptionType.pstPrepay) &&
                                    (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_AUTOMATIC_RECHARGE == 1) &&
                                    (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_AMOUNT_TO_RECHARGE > 0))
                                {

                                    int iQuantityToRecharge = oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_AMOUNT_TO_RECHARGE.Value;
                                    if (Math.Abs(iNewBalance) > oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_AMOUNT_TO_RECHARGE.Value)
                                    {
                                        iQuantityToRecharge = oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_AMOUNT_TO_RECHARGE.Value + Math.Abs(iNewBalance);
                                    }

                                    rtRes = PerformPrepayRecharge(ref oUser, iOSType, true, iQuantityToRecharge, false, dLatitude, dLongitude, strAppVersion, 
                                                                      PaymentMeanRechargeCreationType.pmrctAutomaticRecharge,out dRechargeId);
                                    if (rtRes != ResultType.Result_OK)
                                    {
                                        Logger_AddLogMessage(string.Format("ChargeOffstreetOperation::Error AutoRecharging {0} ", rtRes.ToString()), LogLevels.logERROR);
                                        return rtRes;
                                    }

                                    iBalanceAfterRecharge = oUser.USR_BALANCE;
                                    parametersOut["autorecharged"] = "1";
                                }
                                else if ((oUser.USR_SUSCRIPTION_TYPE == (int)PaymentSuscryptionType.pstPerTransaction))
                                {
                                    rtRes = PerformPrepayRecharge(ref oUser, iOSType, false, -iNewBalance, false, dLatitude, dLongitude, strAppVersion, 
                                                                    PaymentMeanRechargeCreationType.pmrctRegularRecharge, out dRechargeId);
                                    if (rtRes != ResultType.Result_OK)
                                    {
                                        Logger_AddLogMessage(string.Format("ChargeOffstreetOperation::Error Charging Rest Of transaction {0} ", rtRes.ToString()), LogLevels.logERROR);
                                        return rtRes;
                                    }
                                    iBalanceAfterRecharge = oUser.USR_BALANCE;
                                    parametersOut["autorecharged"] = "1";
                                }
                                else
                                {
                                    rtRes = ResultType.Result_Error_Not_Enough_Balance;
                                    Logger_AddLogMessage(string.Format("ChargeOffstreetOperation::Error AutoRecharging {0} ", rtRes.ToString()), LogLevels.logERROR);
                                    return rtRes;
                                }
                            }
                            else
                            {
                                rtRes = ResultType.Result_Error_Invalid_Payment_Mean;
                                Logger_AddLogMessage(string.Format("ChargeOffstreetOperation::{0} ", rtRes.ToString()), LogLevels.logERROR);
                                return rtRes;
                            }


                        }
                    }
                    else if ((oUser.USR_BALANCE == 0) &&
                             (oUser.USR_SUSCRIPTION_TYPE == (int)PaymentSuscryptionType.pstPerTransaction))
                    {
                        //Balance is 0 and suscription type is pertransaction

                        if ((oUser.CUSTOMER_PAYMENT_MEAN != null) &&
                            (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_ENABLED == 1) &&
                            (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_VALID == 1))
                        {
                            rtRes = PerformPerTransactionRecharge(ref oUser, iOSType, iTotalQuantity, oGroup.INSTALLATION.CURRENCy.CUR_ID, dLatitude, dLongitude, strAppVersion, out dRechargeId);
                            if (rtRes != ResultType.Result_OK)
                            {
                                Logger_AddLogMessage(string.Format("ChargeOffstreetOperation::Error charging per transaction value {0} ", rtRes.ToString()), LogLevels.logERROR);
                                return rtRes;
                            }

                            //bRestoreBalanceInCaseOfRefund = false;
                            dBalanceCurID = oGroup.INSTALLATION.CURRENCy.CUR_ID;
                            dChangeToApply = 1.0;
                            dChangeFee = 0;
                            iCurrencyChargedQuantity = iTotalQuantity;
                            suscriptionType = PaymentSuscryptionType.pstPerTransaction;
                        }
                        else
                        {
                            rtRes = ResultType.Result_Error_Invalid_Payment_Mean;
                            Logger_AddLogMessage(string.Format("ChargeOffstreetOperation::{0} ", rtRes.ToString()), LogLevels.logERROR);
                            return rtRes;
                        }
                    }
                    else
                    {
                        rtRes = ResultType.Result_Error_Invalid_Payment_Mean;
                        Logger_AddLogMessage(string.Format("ChargeOffstreetOperation::{0} ", rtRes.ToString()), LogLevels.logERROR);
                        return rtRes;
                    }
                }

                //bool bSubstractFromBalance = bRestoreBalanceInCaseOfRefund;

                DateTime? dtUTCEntryDate = geograficAndTariffsRepository.ConvertInstallationDateTimeToUTC(oGroup.GRP_INS_ID, dtEntryDate);
                DateTime? dtUTCNotifyEntryDate = geograficAndTariffsRepository.ConvertInstallationDateTimeToUTC(oGroup.GRP_INS_ID, dtNotifyEntryDate);
                DateTime? dtUTCPaymentDate = null;
                if (dtPaymentDate.HasValue) dtUTCPaymentDate = geograficAndTariffsRepository.ConvertInstallationDateTimeToUTC(oGroup.GRP_INS_ID, dtPaymentDate.Value);
                DateTime? dtUTCEndDate = null;
                if (dtEndDate.HasValue) dtUTCEndDate = geograficAndTariffsRepository.ConvertInstallationDateTimeToUTC(oGroup.GRP_INS_ID, dtEndDate.Value);
                DateTime? dtUTCExitLimitDate = null;
                if (dtExitLimitDate.HasValue) dtUTCExitLimitDate = geograficAndTariffsRepository.ConvertInstallationDateTimeToUTC(oGroup.GRP_INS_ID, dtExitLimitDate.Value);

                bool bConfirmedWs1 = true;
                bool bConfirmedWs2 = true;
                bool bConfirmedWs3 = true;
                if ((oParkingconfiguration.GOWC_OPT_OPERATIONCONFIRM_MODE ?? 0) == 1)
                {
                    bConfirmedWs1 = false;
                    bConfirmedWs2 = false;
                    bConfirmedWs3 = false;
                }

                if (!customersRepository.ChargeOffstreetOperation(ref oUser,
                                                                  iOSType,
                                                                  true,
                                                                  suscriptionType,
                                                                  operationType,
                                                                  strPlate,
                                                                  oGroup.GRP_INS_ID,
                                                                  oGroup.GRP_ID,
                                                                  sLogicalId,
                                                                  sTariff, sGate, sSpaceDesc,
                                                                  dtEntryDate, dtNotifyEntryDate, dtPaymentDate, dtEndDate, dtExitLimitDate,
                                                                  dtUTCEntryDate.Value, dtUTCNotifyEntryDate.Value, dtUTCPaymentDate, dtUTCEndDate, dtUTCExitLimitDate,
                                                                  iTime,
                                                                  iQuantity,
                                                                  oGroup.INSTALLATION.INS_CUR_ID,
                                                                  dBalanceCurID,
                                                                  dChangeToApply,
                                                                  dChangeFee,
                                                                  iCurrencyChargedQuantity,
                                                                  dPercVAT1, dPercVAT2, iPartialVAT1, dPercFEE, iPercFEETopped, iPartialPercFEE, iFixedFEE, iPartialFixedFEE, iTotalQuantity,
                                                                  dRechargeId,
                                                                  bMustNotify,
                                                                  bConfirmedWs1, bConfirmedWs2, bConfirmedWs3,
                                                                  dMobileSessionId,
                                                                  dLatitude, dLongitude, strAppVersion,
                                                                  sDiscountCodes,
                                                                  out dOperationID))
                {

                    Logger_AddLogMessage(string.Format("ChargeOffstreetOperation::Error Inserting Parking Payment for plate {0} ", strPlate), LogLevels.logERROR);
                    return ResultType.Result_Error_Generic;
                }

                parametersOut["newbal"] = oUser.USR_BALANCE;


            }
            catch (Exception e)
            {
                Logger_AddLogException(e, "ChargeOffstreetOperation::Exception", LogLevels.logERROR);
            }

            return rtRes;
        }

        public double GetChangeToApplyFromInstallationCurToUserCur(INSTALLATION oInstallation, USER oUser)
        {
            double dResult = 1.0;


            try
            {

                if (oInstallation.INS_CUR_ID != oUser.USR_CUR_ID)
                {
                    dResult = CCurrencyConvertor.GetChangeToApply(oInstallation.CURRENCy.CUR_ISO_CODE,
                                              oUser.CURRENCy.CUR_ISO_CODE);
                    if (dResult < 0)
                    {
                        Logger_AddLogMessage(string.Format("GetChangeToApplyFromInstallationCurToUserCur::Error getting change from {0} to {1} ", oInstallation.CURRENCy.CUR_ISO_CODE, oUser.CURRENCy.CUR_ISO_CODE), LogLevels.logERROR);
                        return ((int)ResultType.Result_Error_Generic);
                    }
                }

            }
            catch (Exception e)
            {
                dResult = -1.0;
                Logger_AddLogException(e, "GetChangeToApplyFromInstallationCurToUserCur::Exception", LogLevels.logERROR);
            }

            return dResult;
        }

        public ResultType ConfirmCarPayment(GROUPS_OFFSTREET_WS_CONFIGURATION oParkingConfiguration, GROUP oGroup, USER oUser,
                                            string sOpeId, OffstreetOperationIdType oOpeIdType, string sPlate, string sTariff, string sGate,
                                            OffstreetOperationType operationType, int iAmount, int iTime, double dChangeToApply, string sCurIsoCode,
                                            DateTime dtEntryDate, DateTime dtPaymentDate, DateTime dtEndDate, DateTime dtExitLimitDate,
                                            int iOSType, decimal? dMobileSessionId, decimal? dLatitude, decimal? dLongitude, string sAppVersion,
                                            decimal dPercVAT1, decimal dPercVAT2, decimal dPercFEE, int iPercFEETopped, int iFixedFEE,
                                            int iPartialVAT1, int iPartialPercFEE, int iPartialFixedFEE, int iTotalQuantity,
                                            string sDiscountCodes,
                                            ref SortedList parametersOut)
        {
            ResultType rt = ResultType.Result_OK;


            // Get last offstreet operation with the same group id and logical id (<g> and <ope_id>)
            OPERATIONS_OFFSTREET oLastParkOp = null;
            if (!customersRepository.GetLastOperationOffstreetData(oGroup.GRP_ID, sOpeId, out oLastParkOp))
            {
                rt = ResultType.Result_Error_Generic;
                return rt;
            }

            if (oLastParkOp != null && (oLastParkOp.OPEOFF_TYPE == (int)OffstreetOperationType.Exit || oLastParkOp.OPEOFF_TYPE == (int)OffstreetOperationType.OverduePayment) &&
                                       oLastParkOp.OPEOFF_EXIT_LIMIT_DATE.HasValue && oLastParkOp.OPEOFF_EXIT_LIMIT_DATE >= dtPaymentDate)
            {
                rt = ResultType.Result_Error_OperationAlreadyClosed;
                return rt;
            }

            int iEntryCurrencyChargedQuantity = 0;
            decimal dEntryOperationID = -1;
            int iCurrencyChargedQuantity = 0;            
            decimal dOperationID = -1;
            string str3dPartyOpNum = "";
            decimal? dRechargeId;
            bool bRestoreBalanceInCaseOfRefund = true;
            int? iBalanceAfterRecharge = null;
            
            DateTime dtNotifyEntryDate;
            
            if (oLastParkOp == null)
            {
                dtNotifyEntryDate = dtPaymentDate;
                // Add entry offstreet operation
                rt = ChargeOffstreetOperation(OffstreetOperationType.Entry, sPlate, dChangeToApply, 0, 0,
                                              dtEntryDate, dtNotifyEntryDate, null, null, null,
                                              oParkingConfiguration, oGroup, sOpeId, sTariff, sGate, "", false,
                                              ref oUser, iOSType, dMobileSessionId, dLatitude, dLongitude, sAppVersion,
                                              0, 0, 0, 0, 0, 
                                              0, 0, 0, 0,
                                              null,
                                              ref parametersOut, out iEntryCurrencyChargedQuantity, out dEntryOperationID,
                                              out dRechargeId, out iBalanceAfterRecharge, out bRestoreBalanceInCaseOfRefund);

                if (rt != ResultType.Result_OK)
                {
                    return rt;
                }
            }
            else
            {
                dtNotifyEntryDate = oLastParkOp.OPEOFF_NOTIFY_ENTRY_DATE;
            }

            rt = ChargeOffstreetOperation(operationType, sPlate, dChangeToApply, iAmount, iTime,
                                          dtEntryDate, dtNotifyEntryDate, dtPaymentDate, dtEndDate, dtExitLimitDate,
                                          oParkingConfiguration, oGroup, sOpeId, sTariff, sGate, "", false,
                                          ref oUser, iOSType, dMobileSessionId, dLatitude, dLongitude, sAppVersion,
                                          dPercVAT1, dPercVAT2, dPercFEE, iPercFEETopped, iFixedFEE, 
                                          iPartialVAT1, iPartialPercFEE, iPartialFixedFEE, iTotalQuantity,
                                          sDiscountCodes,
                                          ref parametersOut, out iCurrencyChargedQuantity, out dOperationID,
                                          out dRechargeId, out iBalanceAfterRecharge, out bRestoreBalanceInCaseOfRefund);

            if (rt != ResultType.Result_OK)
            {
                return rt;
            }

            ThirdPartyOffstreet oThirdPartyOffstreet = null;
            long lEllapsedTime = -1;

            if ((oParkingConfiguration.GOWC_OPT_OPERATIONCONFIRM_MODE ?? 0) == 0)
            {
                oThirdPartyOffstreet = new ThirdPartyOffstreet();

                switch ((ConfirmExitOffstreetWSSignatureType)oParkingConfiguration.GOWC_EXIT_WS1_SIGNATURE_TYPE)
                {
                    case ConfirmExitOffstreetWSSignatureType.test:
                        {
                            str3dPartyOpNum = "EXT" + dOperationID.ToString();
                            rt = ResultType.Result_OK;
                        }
                        break;

                    case ConfirmExitOffstreetWSSignatureType.meypar:
                        {
                            rt = oThirdPartyOffstreet.MeyparNotifyCarPayment(1, oParkingConfiguration, sOpeId, oOpeIdType, sPlate, iAmount + iPartialVAT1, sCurIsoCode, iTime, dtEntryDate, dtEndDate, sGate, sTariff, dOperationID, ref oUser,
                                                                             ref parametersOut, out str3dPartyOpNum, out lEllapsedTime);
                        }
                        break;

                    case ConfirmExitOffstreetWSSignatureType.no_call:
                        rt = ResultType.Result_OK;
                        break;

                    default:
                        parametersOut["r"] = Convert.ToInt32(ResultType.Result_Error_Generic).ToString();
                        break;
                }
            }


            if (rt != ResultType.Result_OK)
            {

                if (parametersOut.IndexOfKey("autorecharged") >= 0)
                    parametersOut.RemoveAt(parametersOut.IndexOfKey("autorecharged"));
                if (parametersOut.IndexOfKey("newbal") >= 0)
                    parametersOut.RemoveAt(parametersOut.IndexOfKey("newbal"));

                if (dEntryOperationID != -1)
                {
                    if (!customersRepository.RefundChargeOffstreetPayment(ref oUser, false, dEntryOperationID))
                    {
                        Logger_AddLogMessage(string.Format("RefundChargeOffstreetPayment::Error Refunding Entry Offstreet {0} ", dEntryOperationID), LogLevels.logERROR);                        
                    }
                }

                ResultType rtRefund = RefundChargeOffstreetPayment(ref oUser, dOperationID, dRechargeId, bRestoreBalanceInCaseOfRefund);
                if (rtRefund == ResultType.Result_OK)
                {
                    Logger_AddLogMessage(string.Format("ConfirmCarPayment::Payment Refund of {0}", iCurrencyChargedQuantity), LogLevels.logERROR);
                }
                else
                {
                    Logger_AddLogMessage(string.Format("ConfirmCarPayment::Error in Payment Refund: {0}", rtRefund.ToString()), LogLevels.logERROR);
                }

                return rt;
            }
            else
            {
                parametersOut["utc_offset"] = geograficAndTariffsRepository.GetInstallationUTCOffSetInMinutes(oGroup.INSTALLATION.INS_ID);

                if (str3dPartyOpNum.Length > 0)
                {
                    customersRepository.UpdateThirdPartyIDInOffstreetOperation(ref oUser, 1, dOperationID, str3dPartyOpNum);
                }


                if (dRechargeId != null)
                {
                    customersRepository.ConfirmRecharge(ref oUser, dRechargeId.Value);

                    try
                    {
                        CUSTOMER_PAYMENT_MEANS_RECHARGE oRecharge = null;
                        if (customersRepository.GetRechargeData(ref oUser, dRechargeId.Value, out oRecharge))
                        {
                            if ((PaymentSuscryptionType)oRecharge.CUSPMR_SUSCRIPTION_TYPE == PaymentSuscryptionType.pstPrepay)
                            {
                                string culture = oUser.USR_CULTURE_LANG;
                                CultureInfo ci = new CultureInfo(culture);
                                Thread.CurrentThread.CurrentUICulture = ci;
                                Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture(ci.Name);

                                iAmount = oRecharge.CUSPMR_AMOUNT;
                                dPercVAT1 = oRecharge.CUSPMR_PERC_VAT1 ?? 0;
                                dPercVAT2 = oRecharge.CUSPMR_PERC_VAT2 ?? 0;
                                dPercFEE = oRecharge.CUSPMR_PERC_FEE ?? 0;
                                iPercFEETopped = (int)(oRecharge.CUSPMR_PERC_FEE_TOPPED ?? 0);
                                iFixedFEE = (int)(oRecharge.CUSPMR_FIXED_FEE ?? 0);

                                int iPartialPercFEEVAT;
                                int iPartialFixedFEEVAT;

                                iTotalQuantity = customersRepository.CalculateFEE(iAmount, dPercVAT1, dPercVAT2, dPercFEE, iPercFEETopped, iFixedFEE, out iPartialVAT1, out iPartialPercFEE, out iPartialFixedFEE, out iPartialPercFEEVAT, out iPartialFixedFEEVAT);

                                int iQFEE = Convert.ToInt32(Math.Round(iAmount * dPercFEE, MidpointRounding.AwayFromZero));
                                if (iPercFEETopped > 0 && iQFEE > iPercFEETopped) iQFEE = iPercFEETopped;
                                iQFEE += iFixedFEE;
                                int iQVAT = iPartialVAT1 + iPartialPercFEEVAT + iPartialFixedFEEVAT;
                                int iQSubTotal = iAmount + iQFEE;

                                int iLayout = 0;
                                if (iQFEE != 0 || iQVAT != 0)
                                {
                                    OPERATOR oOperator = customersRepository.GetDefaultOperator();
                                    if (oOperator != null) iLayout = oOperator.OPR_FEE_LAYOUT;
                                }


                                string sLayoutSubtotal = "";
                                string sLayoutTotal = "";

                                sCurIsoCode = infraestructureRepository.GetCurrencyIsoCode(Convert.ToInt32(oRecharge.CUSPMR_CUR_ID));

                                if (iLayout == 2)
                                {
                                    sLayoutSubtotal = string.Format(ResourceExtension.GetLiteral("Email_LayoutSubtotal"),
                                                                    string.Format("{0:0.00} {1}", Convert.ToDouble(iQSubTotal) / 100, sCurIsoCode),
                                                                    (oRecharge.CUSPMR_PERC_VAT1 != 0 ? string.Format("{0:0.00}% ", oRecharge.CUSPMR_PERC_VAT1 * 100) : "") +
                                                                    (oRecharge.CUSPMR_PERC_VAT2 != 0 && oRecharge.CUSPMR_PERC_VAT1 != oRecharge.CUSPMR_PERC_VAT2 ? string.Format("{0:0.00}%", oRecharge.CUSPMR_PERC_VAT2 * 100) : ""),
                                                                    string.Format("{0:0.00} {1}", Convert.ToDouble(iQVAT) / 100, sCurIsoCode));
                                }
                                else if (iLayout == 1)
                                {
                                    sLayoutTotal = string.Format(ResourceExtension.GetLiteral("Email_LayoutTotal"),
                                                                 string.Format("{0:0.00} {1}", Convert.ToDouble(iAmount) / 100, sCurIsoCode),
                                                                 string.Format("{0:0.00} {1}", Convert.ToDouble(iQFEE) / 100, sCurIsoCode),
                                                                 (oRecharge.CUSPMR_PERC_VAT1 != 0 ? string.Format("{0:0.00}% ", oRecharge.CUSPMR_PERC_VAT1 * 100) : "") +
                                                                 (oRecharge.CUSPMR_PERC_VAT2 != 0 && oRecharge.CUSPMR_PERC_VAT1 != oRecharge.CUSPMR_PERC_VAT2 ? string.Format("{0:0.00}%", oRecharge.CUSPMR_PERC_VAT2 * 100) : ""),
                                                                 string.Format("{0:0.00} {1}", Convert.ToDouble(iQVAT) / 100, sCurIsoCode));
                                }

                                string strRechargeEmailSubject = ResourceExtension.GetLiteral("ConfirmAutomaticRecharge_EmailHeader");
                                /*
                                    ID: {0}<br>
                                 *  Fecha de recarga: {1:HH:mm:ss dd/MM/yyyy}<br>
                                 *  Cantidad Recargada: {2} 
                                 */
                                string strRechargeEmailBody = string.Format(ResourceExtension.GetLiteral("ConfirmRecharge_EmailBody"),
                                    oRecharge.CUSPMR_ID,
                                    oRecharge.CUSPMR_DATE,
                                    string.Format("{0:0.00} {1}", Convert.ToDouble(oRecharge.CUSPMR_TOTAL_AMOUNT_CHARGED) / 100,
                                                                  infraestructureRepository.GetCurrencyIsoCode(Convert.ToInt32(oRecharge.CUSPMR_CUR_ID))),
                                    string.Format("{0:0.00} {1}", Convert.ToDouble(iBalanceAfterRecharge) / 100,
                                                        infraestructureRepository.GetCurrencyIsoCode(Convert.ToInt32(oUser.USR_CUR_ID))),
                                    ConfigurationManager.AppSettings["EmailSignatureURL"],
                                    ConfigurationManager.AppSettings["EmailSignatureGraphic"],
                                    sLayoutSubtotal, sLayoutTotal,
                                    GetEmailFooter(ref oUser));

                                SendEmail(ref oUser, strRechargeEmailSubject, strRechargeEmailBody);

                            }
                        }
                    }
                    catch { }

                }

                if (oUser.USR_SUSCRIPTION_TYPE == (int)PaymentSuscryptionType.pstPrepay)
                {
                    int iDiscountValue = 0;
                    string strDiscountCurrencyISOCode = "";

                    try
                    {
                        iDiscountValue = Convert.ToInt32(ConfigurationManager.AppSettings["SuscriptionType1_DiscountValue"]);
                        strDiscountCurrencyISOCode = ConfigurationManager.AppSettings["SuscriptionType1_DiscountCurrency"];
                    }
                    catch
                    { }


                    if (iDiscountValue > 0)
                    {
                        double dDiscountChangeApplied = 0;
                        double dDiscountChangeFee = 0;
                        int iCurrencyDiscountQuantity = ChangeQuantityFromCurToUserCur(iDiscountValue, strDiscountCurrencyISOCode, oUser,
                                                                                        out dDiscountChangeApplied, out dDiscountChangeFee);

                        if (iCurrencyDiscountQuantity > 0)
                        {
                            DateTime? dtUTCTime = geograficAndTariffsRepository.ConvertInstallationDateTimeToUTC(oGroup.GRP_INS_ID, dtPaymentDate.AddSeconds(1));

                            /*customersRepository.AddDiscountToParkingOperation(ref oUser, iOSType, PaymentSuscryptionType.pstPrepay,
                                                                                dtPaymentDate.AddSeconds(1), dtUTCTime.Value, iDiscountValue,
                                                                                infraestructureRepository.GetCurrencyFromIsoCode(strDiscountCurrencyISOCode),
                                                                                oUser.CURRENCy.CUR_ID, dDiscountChangeApplied, dDiscountChangeFee, iCurrencyDiscountQuantity, dOperationID,
                                                                                dLatitude, dLongitude, sAppVersion);*/

                            parametersOut["newbal"] = oUser.USR_BALANCE;

                        }
                    }

                }


            }


            if (Convert.ToInt32(parametersOut["r"]) == Convert.ToInt32(ResultType.Result_OK))
            {
                //customersRepository.DeleteSessionOperationOffstreetInfo(ref oUser, parametersIn["SessionID"].ToString());

                try
                {
                    OPERATIONS_OFFSTREET oParkOp = null;
                    if (customersRepository.GetOperationOffstreetData(ref oUser, dOperationID, out oParkOp))
                    {
                        string culture = oUser.USR_CULTURE_LANG;
                        CultureInfo ci = new CultureInfo(culture);
                        Thread.CurrentThread.CurrentUICulture = ci;
                        Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture(ci.Name);

                        iAmount = oParkOp.OPEOFF_AMOUNT;                         
                        dPercVAT1 = oParkOp.OPEOFF_PERC_VAT1 ?? 0;
                        dPercVAT2 = oParkOp.OPEOFF_PERC_VAT2 ?? 0;
                        dPercFEE = oParkOp.OPEOFF_PERC_FEE ?? 0;                        
                        iPercFEETopped = (int)(oParkOp.OPEOFF_PERC_FEE_TOPPED ?? 0);
                        iFixedFEE = (int)(oParkOp.OPEOFF_FIXED_FEE ?? 0);

                        int iPartialPercFEEVAT;
                        int iPartialFixedFEEVAT;

                        if (oParkOp.OPEOFF_PARTIAL_VAT1.HasValue)
                        {
                            iPartialVAT1 = Convert.ToInt32(oParkOp.OPEOFF_PARTIAL_VAT1.Value);
                            iTotalQuantity = customersRepository.CalculateFEE(iAmount, dPercVAT1, dPercVAT2, dPercFEE, iPercFEETopped, iFixedFEE,
                                                                              iPartialVAT1, out iPartialPercFEE, out iPartialFixedFEE,
                                                                              out iPartialPercFEEVAT, out iPartialFixedFEEVAT);
                        }
                        else
                            iTotalQuantity = customersRepository.CalculateFEE(iAmount, dPercVAT1, dPercVAT2, dPercFEE, iPercFEETopped, iFixedFEE,
                                                                              out iPartialVAT1, out iPartialPercFEE, out iPartialFixedFEE,
                                                                              out iPartialPercFEEVAT, out iPartialFixedFEEVAT);

                        int iQFEE = Convert.ToInt32(Math.Round(iAmount * dPercFEE, MidpointRounding.AwayFromZero));
                        if (iPercFEETopped > 0 && iQFEE > iPercFEETopped) iQFEE = iPercFEETopped;
                        iQFEE += iFixedFEE;                        
                        int iQVAT = iPartialVAT1 + iPartialPercFEEVAT + iPartialFixedFEEVAT;
                        int iQSubTotal = iAmount + iQFEE;

                        int iLayout = 0;
                        if (iQFEE != 0 || iQVAT != 0)
                        {
                            iLayout = oParkingConfiguration.GOWC_FEE_LAYOUT;
                        }

                        string sLayoutSubtotal = "";
                        string sLayoutTotal = "";

                        sCurIsoCode = infraestructureRepository.GetCurrencyIsoCode(Convert.ToInt32(oParkOp.OPEOFF_AMOUNT_CUR_ID));

                        if (iLayout == 2)
                        {
                            sLayoutSubtotal = string.Format(ResourceExtension.GetLiteral("Email_LayoutSubtotal"),
                                                            string.Format("{0:0.00} {1}", Convert.ToDouble(iQSubTotal) / 100, sCurIsoCode),
                                                            (oParkOp.OPEOFF_PERC_VAT1 != 0 ? string.Format("{0:0.00}% ", oParkOp.OPEOFF_PERC_VAT1 * 100) : "") +
                                                            (oParkOp.OPEOFF_PERC_VAT2 != 0 && oParkOp.OPEOFF_PERC_VAT1 != oParkOp.OPEOFF_PERC_VAT2 ? string.Format("{0:0.00}%", oParkOp.OPEOFF_PERC_VAT2 * 100) : ""),
                                                            string.Format("{0:0.00} {1}", Convert.ToDouble(iQVAT) / 100, sCurIsoCode));
                        }
                        else if (iLayout == 1)
                        {
                            sLayoutTotal = string.Format(ResourceExtension.GetLiteral("Email_LayoutTotal"),
                                                         string.Format("{0:0.00} {1}", Convert.ToDouble(iAmount) / 100, sCurIsoCode),
                                                         string.Format("{0:0.00} {1}", Convert.ToDouble(iQFEE) / 100, sCurIsoCode),
                                                         (oParkOp.OPEOFF_PERC_VAT1 != 0 ? string.Format("{0:0.00}% ", oParkOp.OPEOFF_PERC_VAT1 * 100) : "") +
                                                         (oParkOp.OPEOFF_PERC_VAT2 != 0 && oParkOp.OPEOFF_PERC_VAT1 != oParkOp.OPEOFF_PERC_VAT2 ? string.Format("{0:0.00}%", oParkOp.OPEOFF_PERC_VAT2 * 100) : ""),
                                                         string.Format("{0:0.00} {1}", Convert.ToDouble(iQVAT) / 100, sCurIsoCode));
                        }

                        string strParkingEmailSubject = ResourceExtension.GetLiteral("ConfirmOffstreet_EmailHeader");
                        /*
                            * ID: {0}<br>
                            * Matr&iacute;cula: {1}<br>
                            * Ciudad: {2}<br>
                            * Zona: {3}<br>
                            * Tarifa: {4}<br>
                            * Fecha de emisi&ocuate;: {5:HH:mm:ss dd/MM/yyyy}<br>
                            * Aparcamiento Comienza:  {6:HH:mm:ss dd/MM/yyyy}<br><b>
                            * Aparcamiento Finaliza:  {7:HH:mm:ss dd/MM/yyyy}</b><br>
                            * Cantidad Pagada: {8} 
                            */
                        INSTALLATION oInstallation = oParkOp.INSTALLATION;
                        string strParkingEmailBody = string.Format(ResourceExtension.GetLiteral("ConfirmOffstreet_EmailBody"),
                            oParkOp.OPEOFF_ID,
                            oParkOp.USER_PLATE.USRP_PLATE,
                            oParkOp.INSTALLATION.INS_DESCRIPTION,
                            oParkOp.GROUP.GRP_DESCRIPTION,
                            oParkOp.OPEOFF_TARIFF,
                            oParkOp.OPEOFF_PAYMENT_DATE,
                            oParkOp.OPEOFF_ENTRY_DATE,
                            oParkOp.OPEOFF_END_DATE,
                            (oParkOp.OPEOFF_AMOUNT_CUR_ID == oParkOp.OPEOFF_BALANCE_CUR_ID ?
                                string.Format("{0:0.00} {1}", Convert.ToDouble(oParkOp.OPEOFF_TOTAL_AMOUNT) / 100, oParkOp.CURRENCy.CUR_ISO_CODE) :
                                string.Format("{0:0.00} {1} / {2:0.00} {3}", Convert.ToDouble(oParkOp.OPEOFF_TOTAL_AMOUNT) / 100, oParkOp.CURRENCy.CUR_ISO_CODE,
                                                                            Convert.ToDouble(oParkOp.OPEOFF_FINAL_AMOUNT) / 100, oParkOp.CURRENCy1.CUR_ISO_CODE)),
                            (oParkOp.OPEOFF_SUSCRIPTION_TYPE == (int)PaymentSuscryptionType.pstPrepay || oUser.USR_BALANCE > 0)?
                                    string.Format(ResourceExtension.GetLiteral("ConfirmOffstreet_EmailBody_Balance"), string.Format("{0:0.00} {1}", 
                                                                                                             Convert.ToDouble(oUser.USR_BALANCE) / 100, 
                                                                                                             infraestructureRepository.GetCurrencyIsoCode(Convert.ToInt32(oUser.USR_CUR_ID)))) : "",
                            ConfigurationManager.AppSettings["EmailSignatureURL"],
                            ConfigurationManager.AppSettings["EmailSignatureGraphic"],
                            sLayoutSubtotal,
                            sLayoutTotal,
                            GetEmailFooter(ref oInstallation));                        

                        SendEmail(ref oUser, strParkingEmailSubject, strParkingEmailBody);
                    }
                }
                catch { }


            }



            if ((oParkingConfiguration.GOWC_OPT_OPERATIONCONFIRM_MODE ?? 0) == 0)
            {

                if (Convert.ToInt32(parametersOut["r"]) == Convert.ToInt32(ResultType.Result_OK))
                {
                    bool bConfirmed1 = true;
                    bool bConfirmed2 = true;
                    bool bConfirmed3 = true;

                    if (oParkingConfiguration.GOWC_EXIT_WS2_SIGNATURE_TYPE.HasValue)
                    {
                        SortedList parametersOutTemp = new SortedList();

                        switch ((ConfirmExitOffstreetWSSignatureType)oParkingConfiguration.GOWC_EXIT_WS2_SIGNATURE_TYPE)
                        {
                            case ConfirmExitOffstreetWSSignatureType.meypar:
                                {
                                    rt = oThirdPartyOffstreet.MeyparNotifyCarPayment(2, oParkingConfiguration, sOpeId, oOpeIdType, sPlate, iAmount + iPartialVAT1, sCurIsoCode, iTime, dtEntryDate, dtEndDate, sGate, sTariff, dOperationID, ref oUser,
                                                                                     ref parametersOut, out str3dPartyOpNum, out lEllapsedTime);
                                }
                                break;

                            case ConfirmExitOffstreetWSSignatureType.no_call:
                                rt = ResultType.Result_OK;
                                break;

                            default:
                                parametersOut["r"] = Convert.ToInt32(ResultType.Result_Error_Generic).ToString();
                                break;
                        }

                        if (rt != ResultType.Result_OK)
                        {
                            bConfirmed2 = false;
                            Logger_AddLogMessage(string.Format("ConfirmCarPayment::Error in WS 2 Confirmation"), LogLevels.logWARN);
                        }
                        else
                        {
                            if (str3dPartyOpNum.Length > 0)
                            {
                                customersRepository.UpdateThirdPartyIDInOffstreetOperation(ref oUser, 2, dOperationID, str3dPartyOpNum);
                            }

                        }
                    }


                    if (oParkingConfiguration.GOWC_EXIT_WS3_SIGNATURE_TYPE.HasValue)
                    {
                        SortedList parametersOutTemp = new SortedList();

                        switch ((ConfirmExitOffstreetWSSignatureType)oParkingConfiguration.GOWC_EXIT_WS3_SIGNATURE_TYPE)
                        {
                            case ConfirmExitOffstreetWSSignatureType.meypar:
                                {
                                    rt = oThirdPartyOffstreet.MeyparNotifyCarPayment(3, oParkingConfiguration, sOpeId, oOpeIdType, sPlate, iAmount + iPartialVAT1, sCurIsoCode, iTime, dtEntryDate, dtEndDate, sGate, sTariff, dOperationID, ref oUser,
                                                                                     ref parametersOut, out str3dPartyOpNum, out lEllapsedTime);
                                }
                                break;

                            case ConfirmExitOffstreetWSSignatureType.no_call:
                                rt = ResultType.Result_OK;
                                break;

                            default:
                                break;
                        }

                        if (rt != ResultType.Result_OK)
                        {
                            bConfirmed3 = false;
                            Logger_AddLogMessage(string.Format("ConfirmCarPayment::Error in WS 3 Confirmation"), LogLevels.logWARN);
                        }
                        else
                        {
                            if (str3dPartyOpNum.Length > 0)
                            {
                                customersRepository.UpdateThirdPartyIDInOffstreetOperation(ref oUser, 3, dOperationID, str3dPartyOpNum);
                            }
                        }
                    }

                    if ((!bConfirmed2) || (!bConfirmed3))
                    {
                        customersRepository.UpdateThirdPartyConfirmedInOffstreetOperation(ref oUser, dOperationID, bConfirmed1, bConfirmed2, bConfirmed3);
                    }
                }
            }

            return rt;
        }

        public ResultType ChargeTollMovement(string strPlate, double dChangeToApply, int iQuantity, 
                                              DateTime dtPaymentDate, string sTollTariff, INSTALLATION oInstallation, TOLL oToll,
                                              ref USER oUser, int iOSType, 
                                              decimal dPercVAT1, decimal dPercVAT2, decimal dPercFEE, int iPercFEETopped, int iFixedFEE, 
                                              int iPartialVAT1, int iPartialPercFEE, int iPartialFixedFEE, int iTotalQuantity,
                                              string sExternalId, bool bOnline, ChargeOperationsType eType, string sQr, decimal? dLockMovementId,
                                              ref SortedList parametersOut, out int iCurrencyChargedQuantity, out decimal dMovementID,
                                              out DateTime? dtUTCInsertionDate, out decimal? dRechargeId, out int? iBalanceAfterRecharge, out bool bRestoreBalanceInCaseOfRefund, out DateTime? dtUTCPaymentDate)
        {
            ResultType rtRes = ResultType.Result_OK;
            iCurrencyChargedQuantity = 0;
            double dChangeFee = 0;
            decimal dBalanceCurID = oUser.CURRENCy.CUR_ID;
            dMovementID = -1;
            dRechargeId = null;
            bRestoreBalanceInCaseOfRefund = true;
            PaymentSuscryptionType suscriptionType = PaymentSuscryptionType.pstPrepay;
            iBalanceAfterRecharge = null;
            dtUTCInsertionDate = null;
            dtUTCPaymentDate = null;

            try
            {                
                parametersOut["autorecharged"] = "0";
                iCurrencyChargedQuantity = ChangeQuantityFromInstallationCurToUserCur(iTotalQuantity /*iQuantity*/, dChangeToApply, oInstallation, oUser, out dChangeFee);

                if (iCurrencyChargedQuantity < 0)
                {
                    rtRes = (ResultType)iCurrencyChargedQuantity;
                    Logger_AddLogMessage(string.Format("ChargeTollMovement::Error Changing quantity {0} ", rtRes.ToString()), LogLevels.logERROR);
                    return rtRes;
                }

                if (eType != ChargeOperationsType.TollUnlock)
                {

                    int iMinimumBalanceAllowed = oInstallation.INS_MAX_UNPAID_BALANCE;

                    if ((oUser.USR_BALANCE > 0) ||
                        (oUser.USR_SUSCRIPTION_TYPE == (int)PaymentSuscryptionType.pstPrepay))
                    {
                        int iNewBalance = oUser.USR_BALANCE - iCurrencyChargedQuantity;

                        if (iNewBalance < 0)
                        {

                            if ((oUser.CUSTOMER_PAYMENT_MEAN != null) &&
                                (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_ENABLED == 1) &&
                                (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_VALID == 1))
                            {


                                if ((oUser.USR_SUSCRIPTION_TYPE == (int)PaymentSuscryptionType.pstPrepay) &&
                                    (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_AUTOMATIC_RECHARGE == 1) &&
                                    (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_AMOUNT_TO_RECHARGE > 0))
                                {

                                    int iQuantityToRecharge = oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_AMOUNT_TO_RECHARGE.Value;
                                    if (Math.Abs(iNewBalance) > oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_AMOUNT_TO_RECHARGE.Value)
                                    {
                                        iQuantityToRecharge = oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_AMOUNT_TO_RECHARGE.Value + Math.Abs(iNewBalance);
                                    }

                                    rtRes = PerformPrepayRecharge(ref oUser, iOSType, true, iQuantityToRecharge, false, null, null, null,
                                                                    PaymentMeanRechargeCreationType.pmrctAutomaticRecharge, out dRechargeId);
                                    if (rtRes != ResultType.Result_OK)
                                    {
                                        Logger_AddLogMessage(string.Format("ChargeTollMovement::Error AutoRecharging {0} ", rtRes.ToString()), LogLevels.logERROR);
                                        if (bOnline)
                                            return rtRes;
                                    }
                                    else
                                    {
                                        iBalanceAfterRecharge = oUser.USR_BALANCE;
                                        parametersOut["autorecharged"] = "1";
                                    }
                                }
                                else if ((oUser.USR_SUSCRIPTION_TYPE == (int)PaymentSuscryptionType.pstPerTransaction))
                                {
                                    rtRes = PerformPrepayRecharge(ref oUser, iOSType, false, -iNewBalance, false, null, null, null,
                                                PaymentMeanRechargeCreationType.pmrctRegularRecharge, out dRechargeId);
                                    if (rtRes != ResultType.Result_OK)
                                    {
                                        Logger_AddLogMessage(string.Format("ChargeTollMovement::Error Charging Rest Of transaction {0} ", rtRes.ToString()), LogLevels.logERROR);
                                        if (bOnline) return rtRes;
                                    }
                                    else
                                    {
                                        iBalanceAfterRecharge = oUser.USR_BALANCE;
                                        parametersOut["autorecharged"] = "1";
                                    }
                                }
                                else if (!bOnline || (iNewBalance >= iMinimumBalanceAllowed && oUser.USR_BALANCE >= 0))
                                {

                                }
                                else
                                {
                                    rtRes = ResultType.Result_Error_Not_Enough_Balance;
                                    Logger_AddLogMessage(string.Format("ChargeTollMovement::Error AutoRecharging {0} ", rtRes.ToString()), LogLevels.logERROR);
                                    if (bOnline) return rtRes;
                                }
                            }
                            else
                            {
                                rtRes = ResultType.Result_Error_Invalid_Payment_Mean;
                                Logger_AddLogMessage(string.Format("ChargeTollMovement::{0} ", rtRes.ToString()), LogLevels.logERROR);
                                if (bOnline) return rtRes;
                            }

                        }
                    }
                    else if ((oUser.USR_BALANCE <= 0) &&
                             (oUser.USR_SUSCRIPTION_TYPE == (int)PaymentSuscryptionType.pstPerTransaction))
                    {
                        //Balance is 0 and suscription type is pertransaction

                        if ((oUser.CUSTOMER_PAYMENT_MEAN != null) &&
                            (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_ENABLED == 1) &&
                            (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_VALID == 1))
                        {
                            rtRes = PerformPerTransactionRecharge(ref oUser, iOSType, iTotalQuantity/*iQuantity*/, oInstallation.CURRENCy.CUR_ID, null, null, null, out dRechargeId);
                            if (rtRes != ResultType.Result_OK)
                            {
                                Logger_AddLogMessage(string.Format("ChargeTollMovement::Error charging per transaction value {0} ", rtRes.ToString()), LogLevels.logERROR);
                                if (bOnline) return rtRes;
                            }
                            else
                            {
                                //bRestoreBalanceInCaseOfRefund = false;
                                dBalanceCurID = oInstallation.CURRENCy.CUR_ID;
                                dChangeToApply = 1.0;
                                dChangeFee = 0;
                                iCurrencyChargedQuantity = iTotalQuantity/*iQuantity*/;
                                suscriptionType = PaymentSuscryptionType.pstPerTransaction;
                            }
                        }
                        else
                        {
                            rtRes = ResultType.Result_Error_Invalid_Payment_Mean;
                            Logger_AddLogMessage(string.Format("ChargeTollMovement::{0} ", rtRes.ToString()), LogLevels.logERROR);
                            if (bOnline) return rtRes;
                        }

                    }
                    else
                    {
                        rtRes = ResultType.Result_Error_Invalid_Payment_Mean;
                        Logger_AddLogMessage(string.Format("ChargeTollMovement::{0} ", rtRes.ToString()), LogLevels.logERROR);
                        if (bOnline) return rtRes;
                    }

                }

                //bool bSubstractFromBalance = bRestoreBalanceInCaseOfRefund;

                dtUTCPaymentDate = geograficAndTariffsRepository.ConvertInstallationDateTimeToUTC(oInstallation.INS_ID, dtPaymentDate);

                decimal? dTollId = null;
                if (oToll != null) dTollId = oToll.TOL_ID;

                if (!customersRepository.ChargeTollMovement(ref oUser,
                                                          iOSType,
                                                          true,
                                                          suscriptionType,                                                          
                                                          strPlate,
                                                          oInstallation.INS_ID,
                                                          dTollId,
                                                          sTollTariff,
                                                          dtPaymentDate,
                                                          dtUTCPaymentDate.Value,
                                                          iQuantity,
                                                          oInstallation.INS_CUR_ID,
                                                          dBalanceCurID,
                                                          dChangeToApply,
                                                          dChangeFee,
                                                          iCurrencyChargedQuantity,
                                                          dPercVAT1, dPercVAT2, iPartialVAT1, dPercFEE, iPercFEETopped, iPartialPercFEE, iFixedFEE, iPartialFixedFEE, iTotalQuantity,                                                          
                                                          dRechargeId,                                                                                                                    
                                                          //strAppVersion,
                                                          sExternalId,
                                                          eType,
                                                          sQr,
                                                          dLockMovementId,
                                                          out dMovementID,
                                                          out dtUTCInsertionDate))
                {

                    Logger_AddLogMessage(string.Format("ChargeTollMovement::Error Inserting Toll Payment for plate {0} ", strPlate), LogLevels.logERROR);
                    return ResultType.Result_Error_Generic;
                }

                parametersOut["newbal"] = oUser.USR_BALANCE;

                if (!bOnline && rtRes != ResultType.Result_OK)
                {                    
                    Logger_AddLogMessage(string.Format("ChargeTollMovement::Online=false, rtRes={0}. Force result ok: rtRes={1}.", rtRes.ToString(), ResultType.Result_OK.ToString()), LogLevels.logWARN);
                    rtRes = ResultType.Result_OK;
                }

            }
            catch (Exception e)
            {
                Logger_AddLogException(e, "ChargeTollMovement::Exception", LogLevels.logERROR);
            }


            return rtRes;
        }

        /*public ResultType ModifyTollMovement(decimal dMovementId, double dChangeToApply, int iQuantity,
                                              string sTollTariff, TOLL oToll,
                                              ref USER oUser, int iOSType,
                                              decimal dPercVAT1, decimal dPercVAT2, decimal dPercFEE, int iPercFEETopped, int iFixedFEE,
                                              int iPartialVAT1, int iPartialPercFEE, int iPartialFixedFEE, int iTotalQuantity,
                                              int iPreviousTotalQuantity,
                                              string sExternalId, bool bOnline, TollMovementType eType, string sQr,
                                              out decimal? dRechargeId, out int? iBalanceAfterRecharge, out bool bRestoreBalanceInCaseOfRefund)
        {
            ResultType rtRes = ResultType.Result_OK;            
            double dChangeFee = 0;
            decimal dBalanceCurID = oUser.CURRENCy.CUR_ID;            
            dRechargeId = null;
            bRestoreBalanceInCaseOfRefund = true;            
            iBalanceAfterRecharge = null;

            try
            {                
                int iCurrencyChargedQuantity = ChangeQuantityFromInstallationCurToUserCur(iTotalQuantity, dChangeToApply, oToll.INSTALLATION, oUser, out dChangeFee);

                if (iCurrencyChargedQuantity < 0)
                {
                    rtRes = (ResultType)iCurrencyChargedQuantity;
                    Logger_AddLogMessage(string.Format("ModifyTollMovement::Error Changing quantity {0} ", rtRes.ToString()), LogLevels.logERROR);
                    return rtRes;
                }

                int iMinimumBalanceAllowed = oToll.INSTALLATION.INS_MAX_UNPAID_BALANCE;

                int iCurrentChargedQuantityDiff = iCurrencyChargedQuantity - iPreviousTotalQuantity;

                if ((oUser.USR_BALANCE > 0) ||
                    (oUser.USR_SUSCRIPTION_TYPE == (int)PaymentSuscryptionType.pstPrepay))
                {
                    int iNewBalance = oUser.USR_BALANCE - iCurrentChargedQuantityDiff;

                    if (iNewBalance < 0)
                    {

                        if ((oUser.CUSTOMER_PAYMENT_MEAN != null) &&
                            (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_ENABLED == 1) &&
                            (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_VALID == 1))
                        {


                            if ((oUser.USR_SUSCRIPTION_TYPE == (int)PaymentSuscryptionType.pstPrepay) &&
                                (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_AUTOMATIC_RECHARGE == 1) &&
                                (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_AMOUNT_TO_RECHARGE > 0))
                            {

                                int iQuantityToRecharge = oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_AMOUNT_TO_RECHARGE.Value;
                                if (Math.Abs(iNewBalance) > oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_AMOUNT_TO_RECHARGE.Value)
                                {
                                    iQuantityToRecharge = oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_AMOUNT_TO_RECHARGE.Value + Math.Abs(iNewBalance);
                                }

                                rtRes = PerformPrepayRecharge(ref oUser, iOSType, true, iQuantityToRecharge, false, null, null, null,
                                                                PaymentMeanRechargeCreationType.pmrctAutomaticRecharge, out dRechargeId);
                                if (rtRes != ResultType.Result_OK)
                                {
                                    Logger_AddLogMessage(string.Format("ModifyTollMovement::Error AutoRecharging {0} ", rtRes.ToString()), LogLevels.logERROR);
                                    if (bOnline)
                                        return rtRes;
                                }
                                else
                                {
                                    iBalanceAfterRecharge = oUser.USR_BALANCE;                                    
                                }
                            }
                            else if ((oUser.USR_SUSCRIPTION_TYPE == (int)PaymentSuscryptionType.pstPerTransaction))
                            {
                                rtRes = PerformPrepayRecharge(ref oUser, iOSType, false, -iNewBalance, false, null, null, null,
                                            PaymentMeanRechargeCreationType.pmrctRegularRecharge, out dRechargeId);
                                if (rtRes != ResultType.Result_OK)
                                {
                                    Logger_AddLogMessage(string.Format("ModifyTollMovement::Error Charging Rest Of transaction {0} ", rtRes.ToString()), LogLevels.logERROR);
                                    if (bOnline) return rtRes;
                                }
                                else
                                {
                                    iBalanceAfterRecharge = oUser.USR_BALANCE;                                    
                                }
                            }
                            else if (!bOnline || (iNewBalance >= iMinimumBalanceAllowed && oUser.USR_BALANCE >= 0))
                            {

                            }
                            else
                            {
                                rtRes = ResultType.Result_Error_Not_Enough_Balance;
                                Logger_AddLogMessage(string.Format("ModifyTollMovement::Error AutoRecharging {0} ", rtRes.ToString()), LogLevels.logERROR);
                                if (bOnline) return rtRes;
                            }
                        }
                        else
                        {
                            rtRes = ResultType.Result_Error_Invalid_Payment_Mean;
                            Logger_AddLogMessage(string.Format("ModifyTollMovement::{0} ", rtRes.ToString()), LogLevels.logERROR);
                            if (bOnline) return rtRes;
                        }

                    }
                }
                else if ((oUser.USR_BALANCE <= 0) &&
                         (oUser.USR_SUSCRIPTION_TYPE == (int)PaymentSuscryptionType.pstPerTransaction))
                {
                    //Balance is 0 and suscription type is pertransaction

                    if ((oUser.CUSTOMER_PAYMENT_MEAN != null) &&
                        (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_ENABLED == 1) &&
                        (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_VALID == 1))
                    {
                        rtRes = PerformPerTransactionRecharge(ref oUser, iOSType, iTotalQuantity, oToll.INSTALLATION.CURRENCy.CUR_ID, null, null, null, out dRechargeId);
                        if (rtRes != ResultType.Result_OK)
                        {
                            Logger_AddLogMessage(string.Format("ModifyTollMovement::Error charging per transaction value {0} ", rtRes.ToString()), LogLevels.logERROR);
                            if (bOnline) return rtRes;
                        }
                        else
                        {
                            bRestoreBalanceInCaseOfRefund = false;
                            dBalanceCurID = oToll.INSTALLATION.CURRENCy.CUR_ID;
                            dChangeToApply = 1.0;
                            dChangeFee = 0;
                            iCurrencyChargedQuantity = iTotalQuantity;
                        }
                    }
                    else
                    {
                        rtRes = ResultType.Result_Error_Invalid_Payment_Mean;
                        Logger_AddLogMessage(string.Format("ModifyTollMovement::{0} ", rtRes.ToString()), LogLevels.logERROR);
                        if (bOnline) return rtRes;
                    }

                }
                else
                {
                    rtRes = ResultType.Result_Error_Invalid_Payment_Mean;
                    Logger_AddLogMessage(string.Format("ModifyTollMovement::{0} ", rtRes.ToString()), LogLevels.logERROR);
                    if (bOnline) return rtRes;
                }

                bool bSubstractFromBalance = bRestoreBalanceInCaseOfRefund;
                

                if (!customersRepository.ModifyTollMovement(dMovementId,
                                                            ref oUser,                                                            
                                                            bSubstractFromBalance,
                                                            oToll.TOL_ID,
                                                            sTollTariff,
                                                            iQuantity,
                                                            iCurrencyChargedQuantity,
                                                            dPercVAT1, dPercVAT2, iPartialVAT1, dPercFEE, iPercFEETopped, iPartialPercFEE, iFixedFEE, iPartialFixedFEE, iTotalQuantity,
                                                            dRechargeId,
                                                            sExternalId,
                                                            eType,
                                                            sQr))
                {

                    Logger_AddLogMessage(string.Format("ModifyTollMovement::Error modifying Toll Payment {0} ", dMovementId), LogLevels.logERROR);
                    return ResultType.Result_Error_Generic;
                }

                //parametersOut["newbal"] = oUser.USR_BALANCE;

                if (!bOnline && rtRes != ResultType.Result_OK)
                {
                    Logger_AddLogMessage(string.Format("ModifyTollMovement::Onlin=false, rtRes={0}. Force result ok: rtRes={1}.", rtRes.ToString(), ResultType.Result_OK.ToString()), LogLevels.logWARN);
                    rtRes = ResultType.Result_OK;
                }

            }
            catch (Exception e)
            {
                Logger_AddLogException(e, "ModifyTollMovement::Exception", LogLevels.logERROR);
            }


            return rtRes;
        }*/

        #endregion

        #region Private Methods

        private int ChangeQuantityFromInstallationCurToUserCur(int iQuantity, double dChangeToApply, INSTALLATION oInstallation, USER oUser, out double dChangeFee)
        {
            int iResult = iQuantity;
            dChangeFee = 0;

            try
            {

                if (oInstallation.INS_CUR_ID != oUser.USR_CUR_ID)
                {
                    double dConvertedValue = Convert.ToDouble(iQuantity) * dChangeToApply;
                    dConvertedValue = Math.Round(dConvertedValue, 4);

                    dChangeFee = Convert.ToDouble(infraestructureRepository.GetChangeFeePerc()) * dConvertedValue / 100;
                    iResult = Convert.ToInt32(dConvertedValue - dChangeFee + 0.5);
                }

            }
            catch (Exception e)
            {
                Logger_AddLogException(e, "ChangeQuantityFromInstallationCurToUserCur::Exception", LogLevels.logERROR);
            }

            return iResult;
        }

        private int ChangeQuantityFromCurToUserCur(int iQuantity, string strISOCode, USER oUser, out double dChangeApplied, out double dChangeFee)
        {
            int iResult = iQuantity;
            dChangeApplied = 1;
            dChangeFee = 0;


            try
            {

                if (strISOCode != oUser.CURRENCy.CUR_ISO_CODE)
                {
                    double dConvertedValue = CCurrencyConvertor.ConvertCurrency(Convert.ToDouble(iQuantity),
                                              strISOCode,
                                              oUser.CURRENCy.CUR_ISO_CODE, out dChangeApplied);
                    if (dConvertedValue < 0)
                    {
                        Logger_AddLogMessage(string.Format("ChangeQuantityFromCurToUserCur::Error Converting {0} {1} to {2} ", iQuantity, strISOCode, oUser.CURRENCy.CUR_ISO_CODE), LogLevels.logERROR);
                        return ((int)ResultType.Result_Error_Generic);
                    }

                    dChangeFee = Convert.ToDouble(infraestructureRepository.GetChangeFeePerc()) * dConvertedValue / 100;
                    iResult = Convert.ToInt32(dConvertedValue - dChangeFee + 0.5);
                }

            }
            catch (Exception e)
            {
                Logger_AddLogException(e, "ChangeQuantityFromCurToUserCur::Exception", LogLevels.logERROR);
            }

            return iResult;
        }

        private ResultType PerformPrepayRecharge(ref USER oUser, int iOSType, bool bAutomatic, int iQuantity, bool bAutoconf, decimal? dLatitude, decimal? dLongitude, string strAppVersion, PaymentMeanRechargeCreationType rechargeCreationType, out decimal? dRechargeId)
        {
            ResultType rtRes = ResultType.Result_Error_Generic;
            dRechargeId = null;

            try
            {

                if ((oUser.CUSTOMER_PAYMENT_MEAN != null) &&
                    (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_ENABLED == 1) &&
                    (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_VALID == 1))
                {

                    decimal dPercVAT1 = 0;
                    decimal dPercVAT2 = 0;
                    decimal dPercFEE = 0;
                    int iPercFEETopped = 0;
                    int iFixedFEE = 0;
                    int? iPaymentTypeId = null;
                    int? iPaymentSubtypeId = null;
                    if (oUser.CUSTOMER_PAYMENT_MEAN != null)
                    {
                        iPaymentTypeId = oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_PAT_ID;
                        iPaymentSubtypeId = oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_PAST_ID;
                    }

                    int iQuantityToRecharge = iQuantity;

                    if ((PaymentMeanType)oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_PAT_ID == PaymentMeanType.pmtDebitCreditCard)
                    {
                        if (oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_MIN_CHARGE.HasValue)
                        {
                            if (iQuantity < oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_MIN_CHARGE.Value)
                            {
                                iQuantityToRecharge = oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_MIN_CHARGE.Value;
                            }
                        }
                    }


                    int iPartialVAT1 = 0;
                    int iPartialPercFEE = 0;
                    int iPartialFixedFEE = 0;

                    int iTotalQuantity = 0;

                    NumberFormatInfo numberFormatProvider = new NumberFormatInfo();
                    numberFormatProvider.NumberDecimalSeparator = ".";
                    decimal dQuantity = 0;
                    decimal dQuantityToCharge = 0;


                    if (oUser.USR_SUSCRIPTION_TYPE == (int)PaymentSuscryptionType.pstPrepay)
                    {

                        if (!customersRepository.GetFinantialParams(oUser, "", iPaymentTypeId, iPaymentSubtypeId, ChargeOperationsType.BalanceRecharge,
                                                                    out dPercVAT1, out dPercVAT2, out dPercFEE, out iPercFEETopped, out iFixedFEE))
                        {
                            rtRes = ResultType.Result_Error_Generic;
                            Logger_AddLogMessage(string.Format("PerformPrepayRecharge::Error: Error getting finantial parameters. Result = {0}", rtRes.ToString()), LogLevels.logERROR);
                        }


                        iTotalQuantity = customersRepository.CalculateFEE(iQuantityToRecharge, dPercVAT1, dPercVAT2, dPercFEE, iPercFEETopped, iFixedFEE, out iPartialVAT1, out iPartialPercFEE, out iPartialFixedFEE);

                        dQuantity = Convert.ToDecimal(iQuantityToRecharge, numberFormatProvider) / 100;
                        dQuantityToCharge = Convert.ToDecimal(iTotalQuantity, numberFormatProvider) / 100;
                    }
                    else
                    {
                        iPartialVAT1 = 0;
                        iPartialPercFEE = 0;
                        iPartialFixedFEE = 0;

                        iTotalQuantity = iQuantityToRecharge; // customersRepository.CalculateFEE(iQuantity, dPercVAT1, dPercVAT2, dPercFEE, iPercFEETopped, iFixedFEE, out iPartialVAT1, out iPartialPercFEE, out iPartialFixedFEE);*/                    

                        dQuantity = Convert.ToDecimal(iQuantityToRecharge, numberFormatProvider) / 100;
                        dQuantityToCharge = Convert.ToDecimal(iTotalQuantity, numberFormatProvider) / 100;

                    }




                    if ((PaymentMeanType)oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_PAT_ID == PaymentMeanType.pmtDebitCreditCard)
                    {
                        string strUserReference = null;
                        string strAuthCode = null;
                        string strAuthResult = null;
                        string strAuthResultDesc = "";
                        string strGatewayDate = null;
                        string strTransactionId = null;
                        string strCardScheme = null;
                        string strCFTransactionID = null;

                        bool bPayIsCorrect = false;
                        PaymentMeanRechargeStatus rechargeStatus = (bAutoconf ? PaymentMeanRechargeStatus.Committed : PaymentMeanRechargeStatus.Authorized);

                        if ((PaymentMeanCreditCardProviderType)oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_CREDIT_CARD_PAYMENT_PROVIDER ==
                            PaymentMeanCreditCardProviderType.pmccpCreditCall)
                        {
                            CardEasePayments cardPayment = new CardEasePayments();

                            bPayIsCorrect = cardPayment.AutomaticPayment(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_CC_TERMINAL_ID,
                                                                        oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_CC_TRANSACTION_KEY,
                                                                        oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_CC_CARDEASE_URL,
                                                                        oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_CC_CARDEASE_TIMEOUT.Value,
                                                                        oUser.USR_EMAIL,
                                                                        dQuantityToCharge,
                                                                        oUser.CUSTOMER_PAYMENT_MEAN.CURRENCy.CUR_ISO_CODE,
                                                                        oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_CARD_HASH,
                                                                        oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_CARD_REFERENCE,
                                                                        bAutoconf,
                                                                        out strUserReference,
                                                                        out strAuthCode,
                                                                        out strAuthResult,
                                                                        out strGatewayDate,
                                                                        out strCardScheme,
                                                                        out strTransactionId);
                        }
                        else if ((PaymentMeanCreditCardProviderType)oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_CREDIT_CARD_PAYMENT_PROVIDER ==
                            PaymentMeanCreditCardProviderType.pmccpIECISA)
                        {
                            int iQuantityToRechargeIECISA = Convert.ToInt32(dQuantityToCharge * infraestructureRepository.GetCurrencyDivisorFromIsoCode(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCy.CUR_ISO_CODE));
                            DateTime dtNow = DateTime.Now;
                            IECISAPayments.IECISAErrorCode eErrorCode;
                            DateTime dtUTCNow = DateTime.UtcNow;
                            IECISAPayments cardPayment = new IECISAPayments();
                            string strErrorMessage="";
                            bool bExceptionError = false;

                            cardPayment.StartAutomaticTransaction(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_CF_USER,
                                               oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_CF_MERCHANT_ID,
                                               oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_CF_INSTANCE,
                                               oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_CF_CENTRE_ID,
                                               oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_CF_POS_ID,
                                               oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_SERVICE_URL,
                                               oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_SERVICE_TIMEOUT,
                                               oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_MAC_KEY,
                                               oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_CARD_REFERENCE,
                                               oUser.USR_EMAIL,
                                               iQuantityToRechargeIECISA,
                                               oUser.CUSTOMER_PAYMENT_MEAN.CURRENCy.CUR_ISO_CODE,
                                               infraestructureRepository.GetCurrencyIsoCodeNumericFromIsoCode(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCy.CUR_ISO_CODE),
                                               dtNow,
                                               out eErrorCode,
                                               out strErrorMessage,
                                               out strTransactionId,
                                               out strUserReference,
                                               out bExceptionError);

                            customersRepository.GatewayErrorLogUpdate(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_ID, bExceptionError, (eErrorCode != IECISAPayments.IECISAErrorCode.OK));

                            if (eErrorCode != IECISAPayments.IECISAErrorCode.OK)
                            {
                                string errorCode = eErrorCode.ToString();

                                m_Log.LogMessage(LogLevels.logERROR, string.Format("PerformPrepayRecharge.StartWebTransaction : errorCode={0} ; errorMessage={1}",
                                          errorCode, strErrorMessage));


                            }
                            else
                            {
                                string strRedirectURL = "";
                                cardPayment.GetWebTransactionPaymentTypes(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_SERVICE_URL,
                                                                        oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_SERVICE_TIMEOUT,
                                                                        strTransactionId,
                                                                        out eErrorCode,
                                                                        out strErrorMessage,
                                                                        out strRedirectURL,
                                                                        out bExceptionError);

                                customersRepository.GatewayErrorLogUpdate(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_ID, bExceptionError, (eErrorCode != IECISAPayments.IECISAErrorCode.OK));

                                if (eErrorCode != IECISAPayments.IECISAErrorCode.OK)
                                {
                                    string errorCode = eErrorCode.ToString();

                                    m_Log.LogMessage(LogLevels.logERROR, string.Format("PerformPrepayRecharge.GetWebTransactionPaymentTypes : errorCode={0} ; errorMessage={1}",
                                              errorCode, strErrorMessage));

                                  
                                }
                                else
                                {

                                    customersRepository.StartRecharge(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_ID,
                                                                               oUser.USR_EMAIL,
                                                                               dtUTCNow,
                                                                               dtNow,
                                                                               iQuantityToRecharge,
                                                                               oUser.CUSTOMER_PAYMENT_MEAN.CURRENCy.CUR_ID,
                                                                               "",
                                                                               strUserReference,
                                                                               strTransactionId,
                                                                               "",
                                                                               "",
                                                                               "",
                                                                               PaymentMeanRechargeStatus.Committed);

                                    DateTime? dtTransactionDate = null;                                    
                                    cardPayment.CompleteAutomaticTransaction(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_SERVICE_URL,
                                                                           oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_SERVICE_TIMEOUT,
                                                                           oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_MAC_KEY,
                                                                           strTransactionId,
                                                                          out eErrorCode,
                                                                          out strErrorMessage,
                                                                          out dtTransactionDate,
                                                                          out strCFTransactionID,
                                                                          out strAuthCode,
                                                                          out bExceptionError);

                                    customersRepository.GatewayErrorLogUpdate(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_ID, bExceptionError, (eErrorCode != IECISAPayments.IECISAErrorCode.OK));

                                    if (eErrorCode != IECISAPayments.IECISAErrorCode.OK)
                                    {
                                        string errorCode = eErrorCode.ToString();

                                        m_Log.LogMessage(LogLevels.logERROR, string.Format("PerformPrepayRecharge.GetWebTransactionPaymentTypes : errorCode={0} ; errorMessage={1}",
                                                  errorCode, strErrorMessage));

                                       

                                    }
                                    else
                                    {

                                        strAuthResult = "succeeded";
                                        customersRepository.CompleteStartRecharge(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_ID,
                                                                                  oUser.USR_EMAIL,
                                                                                  strTransactionId,
                                                                                  strAuthResult,
                                                                                  strCFTransactionID,
                                                                                  dtTransactionDate.Value.ToString("HHmmssddMMyyyy"),
                                                                                  strAuthCode,
                                                                                  PaymentMeanRechargeStatus.Committed);
                                        strGatewayDate = dtTransactionDate.Value.ToString("HHmmssddMMyyyy");
                                        rechargeStatus = PaymentMeanRechargeStatus.Committed;
                                        bPayIsCorrect=true;
                                       
                                    }
                                }

                            }

                        }
                        else if ((PaymentMeanCreditCardProviderType)oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_CREDIT_CARD_PAYMENT_PROVIDER ==
                           PaymentMeanCreditCardProviderType.pmccpStripe)
                        {

                            string result = "";
                            string errorMessage = "";
                            string errorCode = "";
                            string strPAN = "";
                            string strExpirationDateMonth = "";
                            string strExpirationDateYear = "";
                            string strCustomerId = oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_CARD_HASH;

                            int iQuantityToRechargeStripe = Convert.ToInt32(dQuantityToCharge * infraestructureRepository.GetCurrencyDivisorFromIsoCode(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCy.CUR_ISO_CODE));
                            bPayIsCorrect = StripePayments.PerformCharge(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.STRIPE_CONFIGURATION.STRCON_SECRET_KEY,
                                                                        oUser.USR_EMAIL,
                                                                        oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_CARD_REFERENCE,
                                                                        ref strCustomerId,
                                                                        iQuantityToRechargeStripe,
                                                                        oUser.CUSTOMER_PAYMENT_MEAN.CURRENCy.CUR_ISO_CODE,
                                                                        bAutoconf,
                                                                        out result,
                                                                        out errorCode,
                                                                        out errorMessage,
                                                                        out strCardScheme,
                                                                        out strPAN,
                                                                        out strExpirationDateMonth,
                                                                        out strExpirationDateYear,
                                                                        out strTransactionId,
                                                                        out strGatewayDate);

                            if (bPayIsCorrect)
                            {
                                strUserReference = strTransactionId;
                                strAuthCode = "";
                                strAuthResult = "succeeded";

                            }
                        }

                        if (bPayIsCorrect)
                        {


                            if (!customersRepository.RechargeUserBalance(ref oUser,
                                            iOSType,
                                            true,
                                            iQuantityToRecharge,
                                            dPercVAT1, dPercVAT2, iPartialVAT1, dPercFEE, iPercFEETopped, iPartialPercFEE, iFixedFEE, iPartialFixedFEE, iTotalQuantity,
                                //Convert.ToInt32(dQuantityToCharge * 100),                                             
                                            oUser.CURRENCy.CUR_ID,
                                            PaymentSuscryptionType.pstPrepay,
                                            rechargeStatus,
                                            rechargeCreationType,
                                //dVAT,
                                            strUserReference,
                                            strTransactionId,
                                            strCFTransactionID,
                                            strGatewayDate,
                                            strAuthCode,
                                            strAuthResult,
                                            strAuthResultDesc,
                                            oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_CARD_HASH,
                                            oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_CARD_REFERENCE,
                                            strCardScheme,
                                            oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_MASKED_CARD_NUMBER,
                                            oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_CARD_EXPIRATION_DATE,
                                            null,
                                            null,
                                            null,
                                            false,
                                            dLatitude,
                                            dLongitude,
                                            strAppVersion,
                                            out dRechargeId))
                            {
                                rtRes = ResultType.Result_Error_Generic;
                                Logger_AddLogMessage(string.Format("PerformPrepayRecharge::Error: Result = {0}", rtRes.ToString()), LogLevels.logERROR);

                            }
                            else
                            {
                                rtRes = ResultType.Result_OK;
                            }

                        }
                        else
                        {
                            if (bAutomatic)
                            {
                                customersRepository.AutomaticRechargeFailure(ref oUser);
                            }
                            rtRes = ResultType.Result_Error_Recharge_Failed;
                            Logger_AddLogMessage(string.Format("PerformPrepayRecharge::Error: Result = {0}", rtRes.ToString()), LogLevels.logERROR);

                        }

                    }
                    else if (((PaymentMeanType)oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_PAT_ID == PaymentMeanType.pmtPaypal) &&
                        (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_AUTOMATIC_RECHARGE == 1))
                    {
                        PayPal.Services.Private.AP.PayResponse PResponse = null;

                        if (!PaypalPayments.PreapprovalPayRequest(oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_PAYPAL_ID,
                                                                oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_PAYPAL_PREAPPROVAL_KEY,
                                                                dQuantityToCharge,
                                                                oUser.CUSTOMER_PAYMENT_MEAN.CURRENCy.CUR_ISO_CODE,
                                                                "en-US",
                                                                "http://localhost",
                                                                "http://localhost",
                                                                out PResponse))
                        {
                            if (bAutomatic)
                            {
                                customersRepository.AutomaticRechargeFailure(ref oUser);
                            }
                            rtRes = ResultType.Result_Error_Recharge_Failed;
                            Logger_AddLogMessage(string.Format("PerformPrepayRecharge::Error: Result = {0}", rtRes.ToString()), LogLevels.logERROR);

                        }
                        else
                        {
                            if (PResponse.paymentExecStatus != "COMPLETED")
                            {
                                rtRes = ResultType.Result_Error_Recharge_Failed;
                                Logger_AddLogMessage(string.Format("PerformPrepayRecharge::Error: Result = {0}", rtRes.ToString()), LogLevels.logERROR);

                            }
                            else
                            {
                                PayPal.Services.Private.AP.PaymentDetailsResponse PDResponse = null;

                                if (PaypalPayments.PreapprovalPayConfirm(PResponse.payKey,
                                                                            "en-US",
                                                                            out PDResponse))
                                {



                                    if (!customersRepository.RechargeUserBalance(ref oUser,
                                                                                iOSType,
                                                                                true,
                                                                                iQuantity,
                                                                                dPercVAT1, dPercVAT2, iPartialVAT1, dPercFEE, iPercFEETopped, iPartialPercFEE, iFixedFEE, iPartialFixedFEE, iTotalQuantity,
                                        //Convert.ToInt32(dQuantityToCharge * 100),
                                                                                oUser.CURRENCy.CUR_ID,
                                                                                PaymentSuscryptionType.pstPrepay,
                                                                                PaymentMeanRechargeStatus.Committed,
                                                                                rechargeCreationType,
                                        //dVAT,
                                                                                null,
                                                                                PDResponse.paymentInfoList[0].transactionId,
                                                                                null,
                                                                                DateTime.Now.ToUniversalTime().ToString(),
                                                                                null,
                                                                                null,
                                                                                null,
                                                                                null,
                                                                                null,
                                                                                null,
                                                                                null,
                                                                                null,
                                                                                null,
                                                                                null,
                                                                                PResponse.payKey,
                                                                                false,
                                                                                dLatitude,
                                                                                dLongitude,
                                                                                strAppVersion,
                                                                                out dRechargeId))
                                    {
                                        rtRes = ResultType.Result_Error_Generic;
                                        Logger_AddLogMessage(string.Format("PerformPrepayRecharge::Error: Result = {0}", rtRes.ToString()), LogLevels.logERROR);

                                    }
                                    else
                                    {
                                        rtRes = ResultType.Result_OK;
                                    }

                                }
                                else
                                {
                                    if (bAutomatic)
                                    {
                                        customersRepository.AutomaticRechargeFailure(ref oUser);
                                    }
                                    rtRes = ResultType.Result_Error_Recharge_Failed;
                                    Logger_AddLogMessage(string.Format("PerformPrepayRecharge::Error: Result = {0}", rtRes.ToString()), LogLevels.logERROR);

                                }
                            }
                        }
                    }
                    else
                    {
                        rtRes = ResultType.Result_Error_Recharge_Not_Possible;
                        Logger_AddLogMessage(string.Format("PerformPrepayRecharge::Error: Result = {0}", rtRes.ToString()), LogLevels.logERROR);
                    }
                }
                else
                {
                    rtRes = ResultType.Result_Error_Invalid_Payment_Mean;
                    Logger_AddLogMessage(string.Format("PerformPrepayRecharge::Error: Result = {0}", rtRes.ToString()), LogLevels.logERROR);
                }

            }
            catch (Exception e)
            {
                rtRes = ResultType.Result_Error_Generic;
                Logger_AddLogException(e, "PerformPrepayRecharge::Exception", LogLevels.logERROR);

            }


            return rtRes;

        }

        private ResultType PerformPerTransactionRecharge(ref USER oUser, int iOSType, int iQuantity, decimal dCurrencyID, decimal? dLatitude, decimal? dLongitude, string strAppVersion, out decimal? dRechargeId)
        {
            ResultType rtRes = ResultType.Result_Error_Generic;
            dRechargeId = null;

            try
            {

                if ((oUser.CUSTOMER_PAYMENT_MEAN != null) &&
                    (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_ENABLED == 1) &&
                    (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_VALID == 1))
                {
                    decimal dPercVAT1 = 0;
                    decimal dPercVAT2 = 0;
                    decimal dPercFEE = 0;
                    int iPercFEETopped = 0;
                    int iFixedFEE = 0;

                    int iQuantityToRecharge = iQuantity;

                    if ((PaymentMeanType)oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_PAT_ID == PaymentMeanType.pmtDebitCreditCard)
                    {
                        if (oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_MIN_CHARGE.HasValue)
                        {
                            if (iQuantity < oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_MIN_CHARGE.Value)
                            {
                                iQuantityToRecharge = oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_MIN_CHARGE.Value;
                            }
                        }
                    }

                    /*int? iPaymentTypeId = null;
                    int? iPaymentSubtypeId = null;
                    if (oUser.CUSTOMER_PAYMENT_MEAN != null)
                    {
                        iPaymentTypeId = oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_PAT_ID;
                        iPaymentSubtypeId = oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_PAST_ID;
                    }

                    if (!customersRepository.GetFinantialParams(oUser, "", iPaymentTypeId, iPaymentSubtypeId, ChargeOperationsType.BalanceRecharge,
                                                                out dPercVAT1, out dPercVAT2, out dPercFEE, out iPercFEETopped, out iFixedFEE))
                    {
                        rtRes = ResultType.Result_Error_Generic;
                        Logger_AddLogMessage(string.Format("PerformPrepayRecharge::Error: Error getting finantial parameters. Result = {0}", rtRes.ToString()), LogLevels.logERROR);
                    }*/

                    int iPartialVAT1 = 0;
                    int iPartialPercFEE = 0;
                    int iPartialFixedFEE = 0;

                    int iTotalQuantity = iQuantityToRecharge; // customersRepository.CalculateFEE(iQuantity, dPercVAT1, dPercVAT2, dPercFEE, iPercFEETopped, iFixedFEE, out iPartialVAT1, out iPartialPercFEE, out iPartialFixedFEE);*/                    

                    NumberFormatInfo numberFormatProvider = new NumberFormatInfo();
                    numberFormatProvider.NumberDecimalSeparator = ".";
                    decimal dQuantity = Convert.ToDecimal(iQuantityToRecharge, numberFormatProvider) / 100;
                    decimal dQuantityToCharge = Convert.ToDecimal(iTotalQuantity, numberFormatProvider) / 100;


                    /*decimal dFeeVal = 0;
                    decimal dFeePerc = 0;

                    customersRepository.GetPaymentMeanFees(ref oUser, out dFeeVal, out dFeePerc);
                    NumberFormatInfo numberFormatProvider = new NumberFormatInfo();
                    numberFormatProvider.NumberDecimalSeparator = ".";
                    decimal dQuantity = Convert.ToDecimal(iQuantity, numberFormatProvider) / 100;
                    decimal dQuantityToCharge = Math.Round(dQuantity + (dQuantity  * dFeePerc / 100 + dFeeVal / 100), 2);*/

                    if ((PaymentMeanType)oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_PAT_ID == PaymentMeanType.pmtDebitCreditCard)
                    {
                        string strUserReference = null;
                        string strAuthCode = null;
                        string strAuthResultDesc = "";
                        string strAuthResult = null;
                        string strGatewayDate = null;
                        string strTransactionId = null;
                        string strCardScheme = null;
                        string strCFTransactionID = null;

                        bool bPayIsCorrect = false;
                        PaymentMeanRechargeStatus rechargeStatus = PaymentMeanRechargeStatus.Authorized;

                        if ((PaymentMeanCreditCardProviderType)oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_CREDIT_CARD_PAYMENT_PROVIDER ==
                            PaymentMeanCreditCardProviderType.pmccpCreditCall)
                        {
                            CardEasePayments cardPayment = new CardEasePayments();

                            bPayIsCorrect = cardPayment.AutomaticPayment(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_CC_TERMINAL_ID,
                                                                        oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_CC_TRANSACTION_KEY,
                                                                        oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_CC_CARDEASE_URL,
                                                                        oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_CC_CARDEASE_TIMEOUT.Value,
                                                                        oUser.USR_EMAIL,
                                                                        dQuantityToCharge,
                                                                        infraestructureRepository.GetCurrencyIsoCode(Convert.ToInt32(dCurrencyID)),
                                                                        oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_CARD_HASH,
                                                                        oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_CARD_REFERENCE,
                                                                        false,
                                                                        out strUserReference,
                                                                        out strAuthCode,
                                                                        out strAuthResult,
                                                                        out strGatewayDate,
                                                                        out strCardScheme,
                                                                        out strTransactionId);
                        }
                        else if ((PaymentMeanCreditCardProviderType)oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_CREDIT_CARD_PAYMENT_PROVIDER ==
                            PaymentMeanCreditCardProviderType.pmccpIECISA)
                        {

                            int iQuantityToRechargeIECISA = Convert.ToInt32(dQuantityToCharge * infraestructureRepository.GetCurrencyDivisorFromIsoCode(infraestructureRepository.GetCurrencyIsoCode(Convert.ToInt32(dCurrencyID))));                       
                            DateTime dtNow = DateTime.Now;
                            IECISAPayments.IECISAErrorCode eErrorCode;
                            DateTime dtUTCNow = DateTime.UtcNow;
                            IECISAPayments cardPayment = new IECISAPayments();
                            string strErrorMessage = "";
                            bool bExceptionError = false;

                            cardPayment.StartAutomaticTransaction(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_CF_USER,
                                               oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_CF_MERCHANT_ID,
                                               oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_CF_INSTANCE,
                                               oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_CF_CENTRE_ID,
                                               oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_CF_POS_ID,
                                               oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_SERVICE_URL,
                                               oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_SERVICE_TIMEOUT,
                                               oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_MAC_KEY,
                                               oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_CARD_REFERENCE,
                                               oUser.USR_EMAIL,
                                               iQuantityToRechargeIECISA,
                                               oUser.CUSTOMER_PAYMENT_MEAN.CURRENCy.CUR_ISO_CODE,
                                               infraestructureRepository.GetCurrencyIsoCodeNumericFromIsoCode(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCy.CUR_ISO_CODE),
                                               dtNow,
                                               out eErrorCode,
                                               out strErrorMessage,
                                               out strTransactionId,
                                               out strUserReference,
                                               out bExceptionError);

                            customersRepository.GatewayErrorLogUpdate(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_ID, bExceptionError, (eErrorCode != IECISAPayments.IECISAErrorCode.OK));

                            if (eErrorCode != IECISAPayments.IECISAErrorCode.OK)
                            {
                                string errorCode = eErrorCode.ToString();

                                m_Log.LogMessage(LogLevels.logERROR, string.Format("PerformPerTransactionRecharge.StartWebTransaction : errorCode={0} ; errorMessage={1}",
                                          errorCode, strErrorMessage));


                            }
                            else
                            {
                                string strRedirectURL = "";
                                cardPayment.GetWebTransactionPaymentTypes(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_SERVICE_URL,
                                                                        oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_SERVICE_TIMEOUT,
                                                                        strTransactionId,
                                                                        out eErrorCode,
                                                                        out strErrorMessage,
                                                                        out strRedirectURL,
                                                                        out bExceptionError);

                                customersRepository.GatewayErrorLogUpdate(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_ID, bExceptionError, (eErrorCode != IECISAPayments.IECISAErrorCode.OK));

                                if (eErrorCode != IECISAPayments.IECISAErrorCode.OK)
                                {
                                    string errorCode = eErrorCode.ToString();

                                    m_Log.LogMessage(LogLevels.logERROR, string.Format("PerformPerTransactionRecharge.GetWebTransactionPaymentTypes : errorCode={0} ; errorMessage={1}",
                                              errorCode, strErrorMessage));


                                }
                                else
                                {
                                    customersRepository.StartRecharge(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_ID,
                                                                              oUser.USR_EMAIL,
                                                                              dtUTCNow,
                                                                              dtNow,
                                                                              iQuantityToRecharge,
                                                                              oUser.CUSTOMER_PAYMENT_MEAN.CURRENCy.CUR_ID,
                                                                              "",
                                                                              strUserReference,
                                                                              strTransactionId,
                                                                              "",
                                                                              "",
                                                                              "",
                                                                              PaymentMeanRechargeStatus.Committed);

                                    DateTime? dtTransactionDate = null;
                                    cardPayment.CompleteAutomaticTransaction(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_SERVICE_URL,
                                                           oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_SERVICE_TIMEOUT,
                                                           oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.IECISA_CONFIGURATION.IECCON_MAC_KEY,
                                                           strTransactionId,
                                                          out eErrorCode,
                                                          out strErrorMessage,
                                                          out dtTransactionDate,
                                                          out strCFTransactionID,
                                                          out strAuthCode,
                                                          out bExceptionError);

                                    customersRepository.GatewayErrorLogUpdate(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_ID, bExceptionError, (eErrorCode != IECISAPayments.IECISAErrorCode.OK));

                                    if (eErrorCode != IECISAPayments.IECISAErrorCode.OK)
                                    {
                                        string errorCode = eErrorCode.ToString();

                                        m_Log.LogMessage(LogLevels.logERROR, string.Format("PerformPerTransactionRecharge.GetWebTransactionPaymentTypes : errorCode={0} ; errorMessage={1}",
                                                  errorCode, strErrorMessage));



                                    }
                                    else
                                    {

                                        strAuthResult = "succeeded";
                                        customersRepository.CompleteStartRecharge(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.CPTGC_ID,
                                                                                  oUser.USR_EMAIL,
                                                                                  strTransactionId,
                                                                                  strAuthResult,
                                                                                  strCFTransactionID,
                                                                                  dtTransactionDate.Value.ToString("HHmmssddMMyyyy"),
                                                                                  strAuthCode,
                                                                                  PaymentMeanRechargeStatus.Committed);
                                        strGatewayDate = dtTransactionDate.Value.ToString("HHmmssddMMyyyy");
                                        rechargeStatus = PaymentMeanRechargeStatus.Committed;
                                        bPayIsCorrect = true;

                                    }
                                }

                            }

                        }
                        else if ((PaymentMeanCreditCardProviderType)oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_CREDIT_CARD_PAYMENT_PROVIDER ==
                                                PaymentMeanCreditCardProviderType.pmccpStripe)
                        {

                            string result = "";
                            string errorMessage = "";
                            string errorCode = "";
                            string strPAN = "";
                            string strExpirationDateMonth = "";
                            string strExpirationDateYear = "";
                            string strCustomerId = oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_CARD_HASH;

                            int iQuantityToRechargeStripe = Convert.ToInt32(dQuantityToCharge * infraestructureRepository.GetCurrencyDivisorFromIsoCode(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCy.CUR_ISO_CODE));
                            bPayIsCorrect = StripePayments.PerformCharge(oUser.CUSTOMER_PAYMENT_MEAN.CURRENCIES_PAYMENT_TYPE_GATEWAY_CONFIG.STRIPE_CONFIGURATION.STRCON_SECRET_KEY,
                                                                        oUser.USR_EMAIL,
                                                                        oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_CARD_REFERENCE,
                                                                        ref strCustomerId,
                                                                        iQuantityToRechargeStripe,
                                                                        oUser.CUSTOMER_PAYMENT_MEAN.CURRENCy.CUR_ISO_CODE,
                                                                        false,
                                                                        out result,
                                                                        out errorCode,
                                                                        out errorMessage,
                                                                        out strCardScheme,
                                                                        out strPAN,
                                                                        out strExpirationDateMonth,
                                                                        out strExpirationDateYear,
                                                                        out strTransactionId,
                                                                        out strGatewayDate);

                            if (bPayIsCorrect)
                            {
                                strUserReference = strTransactionId;
                                strAuthCode = "";
                                strAuthResult = "succeeded";

                            }
                        }


                        if (bPayIsCorrect)
                        {


                            if (!customersRepository.RechargeUserBalance(ref oUser,
                                            iOSType,
                                            true,
                                            iQuantityToRecharge,
                                            dPercVAT1, dPercVAT2, iPartialVAT1, dPercFEE, iPercFEETopped, iPartialPercFEE, iFixedFEE, iPartialFixedFEE, iTotalQuantity,
                                //Convert.ToInt32(dQuantityToCharge * 100),
                                            dCurrencyID,
                                            PaymentSuscryptionType.pstPerTransaction,
                                            rechargeStatus,
                                            PaymentMeanRechargeCreationType.pmrctRegularRecharge,
                                //0,
                                            strUserReference,
                                            strTransactionId,
                                            strCFTransactionID,
                                            strGatewayDate,
                                            strAuthCode,
                                            strAuthResult,
                                            strAuthResultDesc,
                                            oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_CARD_HASH,
                                            oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_CARD_REFERENCE,
                                            strCardScheme,
                                            oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_MASKED_CARD_NUMBER,
                                            oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_CARD_EXPIRATION_DATE,
                                            null,
                                            null,
                                            null,
                                            false,
                                            dLatitude,
                                            dLongitude,
                                            strAppVersion,
                                            out dRechargeId))
                            {
                                rtRes = ResultType.Result_Error_Generic;
                                Logger_AddLogMessage(string.Format("PerformPerTransactionRecharge::Error: Result = {0}", rtRes.ToString()), LogLevels.logERROR);

                            }
                            else
                            {
                                rtRes = ResultType.Result_OK;
                            }

                        }
                        else
                        {
                            rtRes = ResultType.Result_Error_Recharge_Failed;
                            Logger_AddLogMessage(string.Format("PerformPerTransactionRecharge::Error: Result = {0}", rtRes.ToString()), LogLevels.logERROR);

                        }

                    }
                    else if (((PaymentMeanType)oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_PAT_ID == PaymentMeanType.pmtPaypal) &&
                        (oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_AUTOMATIC_RECHARGE == 1))
                    {
                        PayPal.Services.Private.AP.PayResponse PResponse = null;

                        if (!PaypalPayments.PreapprovalPayRequest(oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_PAYPAL_ID,
                                                                oUser.CUSTOMER_PAYMENT_MEAN.CUSPM_TOKEN_PAYPAL_PREAPPROVAL_KEY,
                                                                dQuantityToCharge,
                                                                infraestructureRepository.GetCurrencyIsoCode(Convert.ToInt32(dCurrencyID)),
                                                                "en-US",
                                                                "http://localhost",
                                                                "http://localhost",
                                                                out PResponse))
                        {
                            rtRes = ResultType.Result_Error_Recharge_Failed;
                            Logger_AddLogMessage(string.Format("PerformPerTransactionRecharge::Error: Result = {0}", rtRes.ToString()), LogLevels.logERROR);

                        }
                        else
                        {
                            if (PResponse.paymentExecStatus != "COMPLETED")
                            {
                                rtRes = ResultType.Result_Error_Recharge_Failed;
                                Logger_AddLogMessage(string.Format("PerformPerTransactionRecharge::Error: Result = {0}", rtRes.ToString()), LogLevels.logERROR);

                            }
                            else
                            {
                                PayPal.Services.Private.AP.PaymentDetailsResponse PDResponse = null;

                                if (PaypalPayments.PreapprovalPayConfirm(PResponse.payKey,
                                                                            "en-US",
                                                                            out PDResponse))
                                {



                                    if (!customersRepository.RechargeUserBalance(ref oUser,
                                                                                iOSType,
                                                                                false,
                                                                                iQuantity,
                                                                                dPercVAT1, dPercVAT2, iPartialVAT1, dPercFEE, iPercFEETopped, iPartialPercFEE, iFixedFEE, iPartialFixedFEE, iTotalQuantity,
                                        //Convert.ToInt32(dQuantityToCharge * 100),
                                                                                dCurrencyID,
                                                                                PaymentSuscryptionType.pstPerTransaction,
                                                                                PaymentMeanRechargeStatus.Committed,
                                                                                PaymentMeanRechargeCreationType.pmrctRegularRecharge,
                                        //0,
                                                                                null,
                                                                                PDResponse.paymentInfoList[0].transactionId,
                                                                                null,
                                                                                DateTime.Now.ToUniversalTime().ToString(),
                                                                                null,
                                                                                null,
                                                                                null,
                                                                                null,
                                                                                null,
                                                                                null,
                                                                                null,
                                                                                null,
                                                                                null,
                                                                                null,
                                                                                PResponse.payKey,
                                                                                false,
                                                                                dLatitude,
                                                                                dLongitude,
                                                                                strAppVersion,
                                                                                out dRechargeId))
                                    {
                                        rtRes = ResultType.Result_Error_Generic;
                                        Logger_AddLogMessage(string.Format("PerformPerTransactionRecharge::Error: Result = {0}", rtRes.ToString()), LogLevels.logERROR);

                                    }
                                    else
                                    {
                                        rtRes = ResultType.Result_OK;
                                    }

                                }
                                else
                                {
                                    rtRes = ResultType.Result_Error_Recharge_Failed;
                                    Logger_AddLogMessage(string.Format("PerformPerTransactionRecharge::Error: Result = {0}", rtRes.ToString()), LogLevels.logERROR);

                                }
                            }
                        }
                    }
                    else
                    {
                        rtRes = ResultType.Result_Error_Recharge_Not_Possible;
                        Logger_AddLogMessage(string.Format("PerformPerTransactionRecharge::Error: Result = {0}", rtRes.ToString()), LogLevels.logERROR);
                    }
                }
                else
                {
                    rtRes = ResultType.Result_Error_Invalid_Payment_Mean;
                    Logger_AddLogMessage(string.Format("PerformPerTransactionRecharge::Error: Result = {0}", rtRes.ToString()), LogLevels.logERROR);
                }

            }
            catch (Exception e)
            {
                rtRes = ResultType.Result_Error_Generic;
                Logger_AddLogException(e, "PerformPerTransactionRecharge::Exception", LogLevels.logERROR);

            }


            return rtRes;

        }


        private ResultType RefundChargeOffstreetPayment(ref USER oUser, decimal dOperationID, decimal? dRechargeID, bool bRestoreBalance)
        {
            ResultType rtRes = ResultType.Result_OK;


            try
            {

                if (!customersRepository.RefundChargeOffstreetPayment(ref oUser,
                                                                bRestoreBalance,
                                                                dOperationID))
                {

                    Logger_AddLogMessage(string.Format("RefundChargeOffstreetPayment::Error Refunding Offstreet Payment {0} ", dOperationID), LogLevels.logERROR);
                    return ResultType.Result_Error_Generic;
                }


                if (dRechargeID != null)
                {
                    if (!customersRepository.RefundRecharge(ref oUser,
                                                            dRechargeID.Value,
                                                            bRestoreBalance))
                    {

                        Logger_AddLogMessage(string.Format("RefundChargeOffstreetPayment::Error Refunding Recharge {0} ", dRechargeID.Value), LogLevels.logERROR);
                        return ResultType.Result_Error_Generic;
                    }
                }

            }
            catch (Exception e)
            {
                Logger_AddLogException(e, "RefundChargeOffstreetPayment::Exception", LogLevels.logERROR);

            }


            return rtRes;
        }

        private bool SendEmail(ref USER oUser, string strEmailSubject, string strEmailBody)
        {
            bool bRes = true;
            try
            {

                long lSenderId = infraestructureRepository.SendEmailTo(oUser.USR_EMAIL, strEmailSubject, strEmailBody);

                if (lSenderId > 0)
                {
                    customersRepository.InsertUserEmail(ref oUser, oUser.USR_EMAIL, strEmailSubject, strEmailBody, lSenderId);
                }

            }
            catch
            {
                bRes = false;
            }

            return bRes;
        }

        private string GetEmailFooter(ref INSTALLATION oInstallation)
        {
            string strFooter = "";

            try
            {
                strFooter = ResourceExtension.GetLiteral(string.Format("footer_INS_{0}", oInstallation.INS_SHORTDESC));
                if (string.IsNullOrEmpty(strFooter))
                {
                    strFooter = ResourceExtension.GetLiteral(string.Format("footer_COU_{0}", oInstallation.COUNTRy.COU_CODE));
                }

            }
            catch
            {

            }

            return strFooter;
        }


        private string GetEmailFooter(ref USER oUser)
        {
            string strFooter = "";

            try
            {
                strFooter = ResourceExtension.GetLiteral(string.Format("footer_CUR_{0}_{1}", oUser.CURRENCy.CUR_ISO_CODE, oUser.COUNTRy.COU_CODE));
                if (string.IsNullOrEmpty(strFooter))
                {
                    strFooter = ResourceExtension.GetLiteral(string.Format("footer_CUR_{0}", oUser.CURRENCy.CUR_ISO_CODE));
                }
            }
            catch
            {

            }

            return strFooter;
        }


        private static void Logger_AddLogMessage(string msg, LogLevels nLevel)
        {
            m_Log.LogMessage(nLevel, msg);
        }

        private static void Logger_AddLogException(Exception ex, string msg, LogLevels nLevel)
        {
            m_Log.LogMessage(nLevel, msg, ex);
        }

        #endregion

    }
}